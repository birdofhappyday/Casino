namespace Ayve.Purchase
{
    using Ayve.Managers;
    using UnityEngine.Purchasing;
    using Debug = Ayve.Debug;

    /// <summary> 구글 플레이 / 애플 앱스토어용 Unity IAP v5 결제 프로바이더. </summary>
    [UnityEngine.Scripting.Preserve]
    public class MobileStorePaymentProvider : IPaymentProvider
    {
        private static readonly System.Collections.Generic.List<string> DefaultPriceOptions = new() { "Null", "0" };

        private readonly System.Collections.Generic.List<string> _pendingConsumeTransactionIds = new System.Collections.Generic.List<string>();
        private PendingOrder _currentOrder;

        private StoreController _storeController;

        // 조회할 때마다 GetProducts()를 훑는 대신, 상품 조회 완료 시점에 한 번 인덱싱해둔다.
        private readonly System.Collections.Generic.Dictionary<string, Product> _productsByStoreId = new();

        private PurchaseResultCallback _pendingCallback;
        private bool _connectFailed;
        private bool _isConnecting;
        private bool _storeConnected;
        private bool _productsReady;
        private bool _pendingState;

        [UnityEngine.Scripting.Preserve]
        public bool IsReady
        {
            get
            {
                if (_connectFailed)
                    Connect();

                return _storeController != null && _storeConnected && _productsReady;
            }
        }

        [UnityEngine.Scripting.Preserve]
        public bool CanReplayInBackground => false;

        [UnityEngine.Scripting.Preserve]
        public void Connect()
        {
            if (_productsReady || _isConnecting)
                return;

            _connectFailed = false;
            if (_storeController == null)
            {
                _storeController = UnityIAPServices.StoreController();
                SubscribeStoreEvents();
            }

            if (_storeConnected)
            {
                FetchProducts();
                return;
            }

            _ = ConnectAsync();
        }

        private async System.Threading.Tasks.Task ConnectAsync()
        {
            _isConnecting = true;

            try
            {
                await _storeController.Connect();
            }
            catch (System.Exception exception)
            {
                _connectFailed = true;
                Debug.LogWarning($"[Purchase] Store connection failed: {exception.Message}");
            }
            finally
            {
                _isConnecting = false;
            }
        }

        private void SubscribeStoreEvents()
        {
            _storeController.OnStoreConnected += OnStoreConnected;
            _storeController.OnStoreDisconnected += OnStoreDisconnected;
            _storeController.OnProductsFetched += OnProductsFetched;
            _storeController.OnProductsFetchFailed += OnProductsFetchFailed;
            _storeController.OnPurchasesFetched += OnPurchasesFetched;
            _storeController.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
            _storeController.OnPurchasePending += OnPurchasePending;
            _storeController.OnPurchaseConfirmed += OnPurchaseConfirmed;
            _storeController.OnPurchaseFailed += OnPurchaseFailed;
            _storeController.OnPurchaseDeferred += OnPurchaseDeferred;
            _storeController.OnAuthAccountChanged += OnAuthAccountChanged;
        }

        [UnityEngine.Scripting.Preserve]
        public void Disconnect()
        {
            _pendingState = false;
            _pendingCallback = null;
        }

        private void OnStoreConnected()
        {
            _storeConnected = true;
            _connectFailed = false;
            FetchProducts();
        }

        private void OnStoreDisconnected(StoreConnectionFailureDescription failure)
        {
            _storeConnected = false;
            _productsReady = false;
            _connectFailed = true;
            Debug.LogWarning($"[Purchase] Store disconnected: {failure.Message}");
        }

        private void FetchProducts()
        {
            var productDefinitions = new System.Collections.Generic.List<ProductDefinition>();
            var itemList = GaManager.Instance.ListStoreProductDataKeys;
            for (int i = 0; i < itemList.Count; i++)
                productDefinitions.Add(new ProductDefinition(itemList[i], ProductType.Consumable));

            _productsReady = false;
            _storeController.FetchProducts(productDefinitions);
        }

        private void OnProductsFetched(System.Collections.Generic.List<Product> products)
        {
            RebuildProductIndex(products);
            _productsReady = true;
            _storeController.FetchPurchases();
        }

        private void OnProductsFetchFailed(ProductFetchFailed failure)
        {
            _productsReady = false;
            _connectFailed = true;
            Debug.LogWarning($"[Purchase] Product fetch failed: {failure.FailureReason}");
        }

        private void OnPurchasesFetched(Orders orders)
        {
            _pendingState = orders.DeferredOrders.Count > 0;
        }

        private void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failure)
        {
            Debug.LogWarning($"[Purchase] Purchase fetch failed: {failure.Message}");
        }

        private void OnAuthAccountChanged()
        {
            _productsByStoreId.Clear();
            _pendingConsumeTransactionIds.Clear();
            _currentOrder = null;
            _pendingCallback = null;
            _pendingState = false;
            _productsReady = false;
            FetchProducts();
        }

        private void RebuildProductIndex(System.Collections.Generic.IEnumerable<Product> products)
        {
            _productsByStoreId.Clear();

            foreach (var product in products)
            {
                if (product?.definition == null)
                    continue;

                _productsByStoreId[product.definition.id] = product;
                _productsByStoreId[product.definition.storeSpecificId] = product;
            }
        }

        private void OnPurchasePending(PendingOrder order)
        {
            var purchasedProduct = GetFirstProduct(order);
            if (purchasedProduct == null)
            {
                Debug.LogWarning("[Purchase] Pending order does not contain a product.");
                var invalidOrderCallback = _pendingCallback;
                _pendingCallback = null;
                invalidOrderCallback?.Invoke(false, "purchase_product_missing");
                return;
            }

            string transactionId = order.Info.TransactionID;
            if (!string.IsNullOrEmpty(transactionId) && _pendingConsumeTransactionIds.Contains(transactionId))
                return;

            _currentOrder = order;
            _pendingState = false;

            var pendingCallback = _pendingCallback;
            _pendingCallback = null;
#if UNITY_IOS
            string purchaseProof = order.Info.Apple?.jwsRepresentation;
            if (string.IsNullOrWhiteSpace(purchaseProof))
            {
                Debug.LogWarning("[Purchase] Apple pending order has no JWS; leaving the transaction unconfirmed.");
                if (pendingCallback != null)
                    pendingCallback.Invoke(false, "purchase_apple_jws_missing");
                else
                    Ayve.Purchase.AyvePurchaseHub.Instance.ClearCheckoutState();
                return;
            }
#else
            string purchaseProof = order.Info.Receipt;
#endif
            if (pendingCallback != null)
            {
                pendingCallback.Invoke(true, purchaseProof);
                return;
            }

            Ayve.Purchase.AyvePurchaseHub.Instance.BuyItemNo = purchasedProduct.definition.id;
#if UNITY_IOS
            Ayve.Purchase.AyvePurchaseHub.Instance.DispatchShopPurchase(General.StoreProductKind.StoreProductAppStore, purchaseProof);
#else
            Ayve.Purchase.AyvePurchaseHub.Instance.DispatchShopPurchasePending(
                General.StoreProductKind.StoreProductAppStore,
                purchaseProof,
                transactionId);
#endif
        }

        private static Product GetFirstProduct(Order order)
        {
            var items = order?.CartOrdered?.Items();
            return items != null && items.Count > 0 ? items[0]?.Product : null;
        }

        private void OnPurchaseConfirmed(Order order)
        {
            if (order is FailedOrder failedOrder)
            {
                Debug.LogWarning($"[Purchase] Purchase confirmation failed: {failedOrder.FailureReason} - {failedOrder.Details}");
                return;
            }

            _currentOrder = null;
        }

        private void OnPurchaseDeferred(DeferredOrder order)
        {
            _pendingState = true;
        }

        private void OnPurchaseFailed(FailedOrder failedOrder)
        {
            _pendingState = false;
            var pendingCallback = _pendingCallback;
            _pendingCallback = null;
            Debug.LogWarning($"[Purchase] Purchase failed: {failedOrder.FailureReason} - {failedOrder.Details}");
            pendingCallback?.Invoke(false, TextDataManager.Instance.GetString(TextDataManager.TextType.Popup, 19));
        }

        [UnityEngine.Scripting.Preserve]
        public void RequestPurchase(string productKey, PurchaseResultCallback callback, bool isRestore = false)
        {
            if (!IsReady)
            {
                Debug.LogWarning($"[Purchase] RequestPurchase failed: not initialized. productId={productKey}");
                callback(false, "store_not_provisioned");
                return;
            }

            if (_pendingState)
            {
                FlushPendingPurchases();
                Debug.LogWarning($"[Purchase] RequestPurchase blocked: deferred state is still active. productId={productKey}");
                callback(false, "purchase_deferred");
                return;
            }

            var product = FindProductByStoreId(productKey);
            if (product == null)
            {
                Debug.LogWarning($"[Purchase] RequestPurchase failed: product lookup returned null. productId={productKey}");
                callback(false, "product_lookup_null");
                return;
            }

            if (!product.availableToPurchase)
            {
                Debug.LogWarning($"[Purchase] RequestPurchase failed: product is not available to purchase. productId={productKey}");
                callback(false, "product_unavailable");
                return;
            }

            _pendingCallback = callback;
            _storeController.PurchaseProduct(product);
        }

        [UnityEngine.Scripting.Preserve]
        public void ReplayLastPurchase(PurchaseResultCallback callback)
        {
            callback?.Invoke(false, null);
        }

        [UnityEngine.Scripting.Preserve]
        public void ConfirmDelivery()
        {
            if (_currentOrder == null)
                return;

            var settledOrder = _currentOrder;
            string transactionId = settledOrder.Info.TransactionID;
            if (!string.IsNullOrEmpty(transactionId) && !_pendingConsumeTransactionIds.Contains(transactionId))
                _pendingConsumeTransactionIds.Add(transactionId);

            _storeController.ConfirmPurchase(settledOrder);
        }

        [UnityEngine.Scripting.Preserve]
        public ulong GetPrice(string productKey)
        {
            if (!IsReady)
                return 0;

            var product = FindProductByStoreId(productKey);
            if (product == null)
                return 0;

            return (ulong)product.metadata.localizedPrice;
        }

        [UnityEngine.Scripting.Preserve]
        public string GetPriceLabel(string productKey)
        {
            if (!IsReady)
                return string.Empty;

            var product = FindProductByStoreId(productKey);
            if (product == null)
                return string.Empty;

            return product.metadata.localizedPriceString;
        }

        [UnityEngine.Scripting.Preserve]
        public System.Collections.Generic.List<string> GetPriceOptions(string productKey) => DefaultPriceOptions;

        // OnProductsFetched에서 미리 인덱싱해둔 캐시를 찾는다 - 카탈로그를 매번 다시 훑지 않는다.
        private Product FindProductByStoreId(string storeId)
        {
            return _productsByStoreId.TryGetValue(storeId, out var product) ? product : null;
        }

        [UnityEngine.Scripting.Preserve]
        public void FlushPendingPurchases()
        {
            if (!IsReady)
                return;

            _pendingState = false;
            _storeController.FetchPurchases();
        }

        [UnityEngine.Scripting.Preserve]
        public void MarkPendingConsumeSuccess(string transactionID)
        {
            if (string.IsNullOrEmpty(transactionID))
                return;

            if (!_pendingConsumeTransactionIds.Contains(transactionID))
                _pendingConsumeTransactionIds.Add(transactionID);
        }

        [UnityEngine.Scripting.Preserve]
        public void MarkCurrentPurchaseSuccess()
        {
            MarkPendingConsumeSuccess(_currentOrder?.Info.TransactionID);
        }
    }
}
