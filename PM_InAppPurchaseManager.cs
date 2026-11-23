namespace Ayve.Purchase
{
    using Ayve.Common;
    using Ayve.Managers;
    using Ayve.Network;
    using General;
    using PmNet;
    using Unity.Services.Core.Environments;
    [UnityEngine.Scripting.Preserve]
    public delegate void PurchaseResultCallback(bool success, string message);

    [UnityEngine.Scripting.Preserve]
    public class AyvePurchaseHub
    {
        private const double PendingPurchaseRecoveryRetryDelaySeconds = 0.75;
        private const int PurchaseRetryCount = 2;
        private int _purchaseAttemptCounter;

        [UnityEngine.Scripting.Preserve]
        private sealed class PurchaseLifecycleTracer : UnityEngine.MonoBehaviour
        {
            public AyvePurchaseHub Owner;

            [UnityEngine.Scripting.Preserve]
            private void OnApplicationFocus(bool focus)
            {
                Owner?.OnApplicationFocusChanged(focus);
            }
        }

        public string GetDisplayPrice(string productKey) => iapBase.GetPriceLabel(productKey);

        private void CancelPendingRecoveryRetry()
        {
            if (pendingRecoveryRetryCoroutine == null || lifecycleTracer == null)
                return;

            lifecycleTracer.StopCoroutine(pendingRecoveryRetryCoroutine);
            pendingRecoveryRetryCoroutine = null;
        }

        private static AyvePurchaseHub instance;
        public static AyvePurchaseHub Instance { get { if (instance == null) instance = new AyvePurchaseHub(); return instance; } }

        public void FinalizeConsumable()
        {
            iapBase.ConfirmDelivery();
            ClearCheckoutState();
        }

        private void EnsureLifecycleTracer()
        {
            if (lifecycleTracer != null)
            {
                lifecycleTracer.Owner = this;
                return;
            }

            var tracerObject = new UnityEngine.GameObject("@IAPLifecycleTracer");
            UnityEngine.Object.DontDestroyOnLoad(tracerObject);
            lifecycleTracer = tracerObject.AddComponent<PurchaseLifecycleTracer>();
            lifecycleTracer.Owner = this;
        }

        private IPaymentProvider iapBase;
        private PurchaseLifecycleTracer lifecycleTracer;
        private PurchaseResultCallback resultDel;
        private bool backgroundedWhilePurchasePending;
        private bool recoveringPendingPurchase;
        private int pendingPurchaseRecoveryAttempt;
        private UnityEngine.Coroutine pendingRecoveryRetryCoroutine;
        public bool IsCheckoutPending { get; private set; }
        public bool IsCashBuy = false;

        public System.Collections.Generic.List<string> getPriceInfoList(string productKey) => iapBase.GetPriceOptions(productKey);

        private void BeginPendingPurchaseRecovery()
        {
            if (iapBase == null || !IsCheckoutPending || resultDel == null)
                return;

            recoveringPendingPurchase = true;
            pendingPurchaseRecoveryAttempt++;
            iapBase.ReplayLastPurchase(onRecoveredPendingPurchase);
        }

        public void Teardown()
        {
            if (iapBase != null)
                iapBase.Disconnect();
            CancelPendingRecoveryRetry();
            backgroundedWhilePurchasePending = false;
            recoveringPendingPurchase = false;
            pendingPurchaseRecoveryAttempt = 0;
            ClearCheckoutState();
        }

        private void onRecoveredPendingPurchase(bool _bSuccess, string _param)
        {
            recoveringPendingPurchase = false;
            if (!IsCheckoutPending)
                return;

            if (resultDel == null)
                return;

            if (_bSuccess == false && pendingPurchaseRecoveryAttempt < PurchaseRetryCount)
            {
                CancelPendingRecoveryRetry();
                pendingRecoveryRetryCoroutine = lifecycleTracer.StartCoroutine(RetryPendingPurchaseRecoveryAfterDelay());
                return;
            }

            HandlePurchaseOutcome(_bSuccess, _param);
        }

        public ulong GetProductPriceLong(string productKey) => iapBase.GetPrice(productKey);

        private System.Collections.IEnumerator RetryPendingPurchaseRecoveryAfterDelay()
        {
            yield return new UnityEngine.WaitForSecondsRealtime((float)PendingPurchaseRecoveryRetryDelaySeconds);

            pendingRecoveryRetryCoroutine = null;
            if (!IsCheckoutPending || resultDel == null || iapBase == null)
                yield break;

            BeginPendingPurchaseRecovery();
        }

        public void CheckPendingConsumes()
        {
            iapBase.FlushPendingPurchases();
        }

        public void CancelActivePurchase()
        {
            _purchaseAttemptCounter = 0;
        }

        public void Bootstrap(bool _forceReinit = false)
        {
            EnsureLifecycleTracer();

            if (iapBase == null || _forceReinit)
            {
    #if UNITY_EDITOR
                iapBase = new EditorPaymentStub();
    #elif ONE_STORE
                    iapBase = new OneStorePaymentProvider();
    #else
                    iapBase = new MobileStorePaymentProvider();
    #endif
            }

            if (iapBase != null)
            {
                IsCheckoutPending = false;
                iapBase.Connect();
            }
        }

        private void OnApplicationFocusChanged(bool focus)
        {
            if (focus == false && IsCheckoutPending)
            {
                backgroundedWhilePurchasePending = true;
                return;
            }

            if (!focus || !backgroundedWhilePurchasePending || !IsCheckoutPending || iapBase == null)
                return;

            backgroundedWhilePurchasePending = false;

            if (!iapBase.CanReplayInBackground || recoveringPendingPurchase || pendingRecoveryRetryCoroutine != null)
                return;

            BeginPendingPurchaseRecovery();
        }

        public void SubmitPurchaseRequest(string _productId, PurchaseResultCallback _del)
        {
            if (IsCheckoutPending == false)
            {
    #if !UNITY_EDITOR
    #endif
                EnsureLifecycleTracer();
                SetIsCheckoutPending();
                backgroundedWhilePurchasePending = false;
                recoveringPendingPurchase = false;
                pendingPurchaseRecoveryAttempt = 0;
                CancelPendingRecoveryRetry();
                resultDel = _del;
                BuyItemNo = _productId;
                _purchaseAttemptCounter++;
                iapBase.RequestPurchase(_productId, HandlePurchaseOutcome);
            }
        }

        public void MarkCurrentPurchaseSuccess()
        {
            if (iapBase is MobileStorePaymentProvider mobile)
                mobile.MarkCurrentPurchaseSuccess();
        }

        public void ClearCheckoutState()
        {
            IsCheckoutPending = false;
            IsCashBuy = false;
        }

        public void SetIsCheckoutPending()
        {
            IsCheckoutPending = true;
        }

        public async void BootstrapServices()
        {
            try
            {
                var options = new Unity.Services.Core.InitializationOptions().SetEnvironmentName("production");

                await Unity.Services.Core.UnityServices.InitializeAsync(options);

                Bootstrap();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void HandlePurchaseOutcome(bool _bSuccess, string _param)
        {
            if (resultDel != null)
                resultDel(_bSuccess, _param);
            resultDel = null;
            CancelPendingRecoveryRetry();
            backgroundedWhilePurchasePending = false;
            recoveringPendingPurchase = false;
            pendingPurchaseRecoveryAttempt = 0;

            if (_bSuccess == false)
            {
                ClearCheckoutState();
            }
        }

        // productId만 실제로 쓰임(구글 결제 영수증 JSON의 실제 키라 이름 변경 불가).
        // 나머지 필드(Payload/signature/orderId/purchaseTime)는 죽은 코드라 제거함 -
        // signature는 파싱 직후 지역변수(receiptSignature)로 바로 덮어써서 실제로는 한 번도 읽히지 않았음.
        [System.Serializable]
        private class PurchaseReceiptDto
        {
            public string productId = string.Empty;
        }

        public void SubmitShopPurchase(General.StoreProductKind marketItemKind, string _receipt)
        {
            DispatchShopPurchase(marketItemKind, _receipt);
        }

        public string BuyItemNo = string.Empty;

        private (string signature, string json) ParseReceiptPayload(string payload)
        {
            var details = (System.Collections.Generic.Dictionary<string, object>)UnityEngine.Purchasing.MiniJson.JsonDecode(payload);
            return ((string)details["signature"], (string)details["json"]);
        }

        private bool IsSandboxPurchase(System.Collections.Generic.Dictionary<string, object> receiptDetails)
        {
            if (receiptDetails == null) return false;
            string purchaseId = (string)receiptDetails["purchaseId"];
            return !string.IsNullOrEmpty(purchaseId) && purchaseId.Contains("SANDBOX");
        }

        public void DispatchShopPurchase(General.StoreProductKind marketItemKind, string _receipt)
        {
            if (marketItemKind == General.StoreProductKind.StoreProductNone) return;
            IsCashBuy = true;

#if UNITY_IOS
            // StoreKit 2 provides a compact JWS, not a Unity receipt JSON wrapper.
            if (marketItemKind != General.StoreProductKind.StoreProductItem)
            {
                DispatchApplePurchase(marketItemKind, _receipt);
                return;
            }
#endif
            Debug.Log($"[Purchase] {marketItemKind}::Receipt : {_receipt}");
            var wrapper = (System.Collections.Generic.Dictionary<string, object>)UnityEngine.Purchasing.MiniJson.JsonDecode(_receipt);

            var data = new PmNet.MarketPurchaseRQ() { };

            if (marketItemKind == General.StoreProductKind.StoreProductItem)
            {
                data.MarketItemKind = marketItemKind;
                data.ItemNo = _receipt;
            }
            else if (wrapper != null)
            {
                string receiptSignature = string.Empty;
                string receiptJson = string.Empty;
                string productID = string.Empty;

                //FinalizeConsumable();
                //endPurchaseWait();

#if ONE_STORE
                    Debug.Log($"[Purchase_OneStore][Receipt] hasJson={wrapper.ContainsKey("json")}, hasSigature={wrapper.ContainsKey("sigature")}, hasSignature={wrapper.ContainsKey("signature")}");
                    receiptJson = (string)wrapper["json"];
                    var receiptDetails = (System.Collections.Generic.Dictionary<string, object>)UnityEngine.Purchasing.MiniJson.JsonDecode(receiptJson);
                    data.SandboxFlag = IsSandboxPurchase(receiptDetails);
                    if (data.SandboxFlag)
                        Debug.Log("[IAP] resolved as sandbox purchase.");
#else
                (receiptSignature, receiptJson) = ParseReceiptPayload((string)wrapper["Payload"]);
    #endif

                bool bStore = marketItemKind != General.StoreProductKind.StoreProductItem;

                PurchaseReceiptDto rec = UnityEngine.JsonUtility.FromJson<PurchaseReceiptDto>(receiptJson);

                marketItemKind = marketItemKind == General.StoreProductKind.StoreProductNone
                    ? General.StoreProductKind.StoreProductAppStore
                    : marketItemKind;

                data.RecvProof = _receipt;
                data.ItemNo = rec.productId;
                data.Sig = receiptSignature;
                data.MarketItemKind = marketItemKind;
            }
            else
            {
                Debug.LogError("[IAP] receipt wrapper decoded to null.");
                return;
            }

            Debug.Log($"[Purchase_OneStore][Send] ItemNo={data.ItemNo}, MarketItemKind={data.MarketItemKind}, SandboxFlag={data.SandboxFlag}, SigEmpty={string.IsNullOrEmpty(data.Sig)}, SigLen={(data.Sig == null ? -1 : data.Sig.Length)}, RecvProofEmpty={string.IsNullOrEmpty(data.RecvProof)}, RecvProofLen={(data.RecvProof == null ? -1 : data.RecvProof.Length)}");
            NtManager.Instance.Dispatch(General.PacketID.PacketStorePurchase, data, Ayve.Network.LegacyIo.IoDirection.Send);
        }

        private void DispatchApplePurchase(General.StoreProductKind _prodType, string _jwsRepresentation)
        {
            if (string.IsNullOrWhiteSpace(_jwsRepresentation))
            {
                Debug.LogWarning("[Purchase] Apple JWS is missing; purchase request was not sent.");
                ClearCheckoutState();
                return;
            }

            var data = new PmNet.MarketPurchaseRQ() { };
            // The server must verify this StoreKit 2 JWS, not call legacy receipt validation.
            data.Sig = _jwsRepresentation;
            data.ItemNo = BuyItemNo;
            data.MarketItemKind = _prodType;

            Debug.Log($"[Purchase_Apple][Send] ItemNo={data.ItemNo}, MarketItemKind={data.MarketItemKind}, JwsLen={_jwsRepresentation.Length}");

            NtManager.Instance.Dispatch(General.PacketID.PacketStorePurchase, data, Ayve.Network.LegacyIo.IoDirection.Send);
        }

        public async void DispatchShopPurchasePending(General.StoreProductKind marketItemKind, string _receipt, string _transactionID)
        {
            if (marketItemKind == General.StoreProductKind.StoreProductNone) return;
            IsCashBuy = true;

#if UNITY_IOS
            if (marketItemKind != General.StoreProductKind.StoreProductItem)
            {
                DispatchApplePurchase(marketItemKind, _receipt);
                return;
            }
#endif
            Debug.Log($"[Purchase] {marketItemKind}::Receipt : {_receipt}");
            var wrapper = (System.Collections.Generic.Dictionary<string, object>)UnityEngine.Purchasing.MiniJson.JsonDecode(_receipt);

            var data = new PmNet.MarketPurchaseRQ() { };

            if (marketItemKind == General.StoreProductKind.StoreProductItem)
            {
                data.MarketItemKind = marketItemKind;
                data.ItemNo = _receipt;
            }
            else if (wrapper != null)
            {
                string receiptSignature = string.Empty;
                string receiptJson = string.Empty;
#if ONE_STORE
                    Debug.Log($"[Purchase_OneStore][Receipt] hasJson={wrapper.ContainsKey("json")}, hasSigature={wrapper.ContainsKey("sigature")}, hasSignature={wrapper.ContainsKey("signature")}");
                    receiptJson = (string)wrapper["json"];
                    //_receipt = receiptJson;
                    var receiptDetails = (System.Collections.Generic.Dictionary<string, object>)UnityEngine.Purchasing.MiniJson.JsonDecode(receiptJson);
                    if (receiptDetails != null)
                    {
                        string purchaseId = (string)receiptDetails["purchaseId"];

                        if (!string.IsNullOrEmpty(purchaseId) && purchaseId.Contains("SANDBOX"))
                        {
                            data.SandboxFlag = true;
                            Debug.Log("[IAP] resolved as sandbox purchase.");
                        }
                    }
#else
                string payload = (string)wrapper["Payload"];
                var receiptDetails = (System.Collections.Generic.Dictionary<string, object>)UnityEngine.Purchasing.MiniJson.JsonDecode(payload);
                receiptSignature = (string)receiptDetails["signature"];
                receiptJson = (string)receiptDetails["json"];
#endif

                PurchaseReceiptDto rec = UnityEngine.JsonUtility.FromJson<PurchaseReceiptDto>(receiptJson);

                if (marketItemKind == General.StoreProductKind.StoreProductNone)
                {
                    marketItemKind = General.StoreProductKind.StoreProductAppStore;
                }

                data.MarketItemKind = marketItemKind;
                data.Sig = receiptSignature;
                //data.ItemNo = TableDataManager.Instance.GetProductID(rec.productId, bStore);
                data.ItemNo = rec.productId;
                data.RecvProof = _receipt;
            }
            else
            {
                Debug.LogError("[IAP] receipt wrapper decoded to null.");
                ClearCheckoutState();
                return;
            }

            Debug.Log($"[Purchase_OneStore][Send] ItemNo={data.ItemNo}, MarketItemKind={data.MarketItemKind}, SandboxFlag={data.SandboxFlag}, SigEmpty={string.IsNullOrEmpty(data.Sig)}, SigLen={(data.Sig == null ? -1 : data.Sig.Length)}, RecvProofEmpty={string.IsNullOrEmpty(data.RecvProof)}, RecvProofLen={(data.RecvProof == null ? -1 : data.RecvProof.Length)}");
            try
            {
                var result = await NtManager.Instance.SendPacketAsync(General.PacketID.PacketStorePurchase, data, Ayve.Network.LegacyIo.IoDirection.Send);
                if (result == null)
                {
                    ClearCheckoutState();
                    return;
                }

                RES_BuyShop(result, _transactionID);
            }
            catch (System.Exception ex)
            {
                ClearCheckoutState();
                Debug.LogException(ex);
            }
        }

        private void RES_BuyShop(object _msg, string _transactionID)
        {
            PktBase proto = _msg as PktBase;

            MarketPurchaseRS res = NtManager.GetData<MarketPurchaseRS>(proto);
            MessagePopupData data = new MessagePopupData()
            {
                ConfirmBtnText = TextDataManager.Instance.GetString(TextDataManager.TextType.Common, 2),
                Message = TextDataManager.Instance.GetString(TextDataManager.TextType.Popup, 19),
            };

            if (proto.ErrKind != General.ResultCode.ResultSuccess)
            {
                if (res != null)
                {
                    if (proto.ErrKind == ResultCode.ResultUsedReceiptPurchaseRejected)
                        data.Message = string.Format("이미 지급된 영수증입니다.\n{0}", _transactionID);
                }

                Debug.Log($"[Purchase_OneStore][ShopBuy] ErrKind={proto.ErrKind}, IsCashBuy={AyvePurchaseHub.Instance.IsCashBuy}");
                if (AyvePurchaseHub.Instance.IsCashBuy)
                    AyvePurchaseHub.Instance.ClearCheckoutState();
                PopupManager.Instance.ShowPopup(eUIType.Message, data);
                return;
            }

            if (AyvePurchaseHub.Instance.IsCashBuy)
            {
#if UNITY_ANDROID
                if (iapBase is MobileStorePaymentProvider mobile)
                    mobile.MarkPendingConsumeSuccess(_transactionID);
#endif
                var req = new RefreshAtriumRQ();
                AyvePurchaseHub.Instance.ClearCheckoutState();
#if UNITY_IOS
                AyvePurchaseHub.Instance.FinalizeConsumable();
#endif
                NtManager.Instance.Dispatch(General.PacketID.PacketLobbyRefresh, req, Ayve.Network.LegacyIo.IoDirection.Send);
            }

            GaManager.Instance.SetPlayerData(res);

            if (GaManager.Instance.GetPlayerData().MembershipTier != BenefitTier.Basic)
            {
                GaManager.Instance.IsMembershipExpired = false;
                PM_EventManager.Publish(UIEvent.RefreshMemberShip, null);
            }

            data.Message = TextDataManager.Instance.GetString(TextDataManager.TextType.Popup, 17);
            PopupManager.Instance.ShowPopup(eUIType.Message, data);
            CheckPendingConsumes();
        }
    }
}
