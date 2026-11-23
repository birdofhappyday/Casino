namespace Ayve.Lobby
{
    using Ayve.Common;
    using Ayve.Data;
    using Ayve.Managers;
    using Ayve.Network;
    using Ayve.Utility;
    using Cysharp.Threading.Tasks;
    using General;
    using Newtonsoft.Json;
    using PmNet;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;
    using Debug = Ayve.Debug;

    public partial class LobbyMainUI : MonoBehaviour
    {
        [SerializeField] private Image m_CoverImage;
        [SerializeField] private float m_LobbyAlphaWaitSecond = 0.1f;
        [SerializeField] private TextMeshProUGUI m_LevelLabel;
        [SerializeField] private TextMeshProUGUI m_NicknameLabel;

        [SerializeField] private Image m_AvatarIcon;
        [SerializeField] private GameObject m_Membership_Standard;
        [SerializeField] private GameObject m_Membership_Premium;

        [SerializeField] private TextMeshProUGUI m_ChipLabel;
        [SerializeField] private TextMeshProUGUI m_CoinLabel;
        [SerializeField] private TextMeshProUGUI m_CashLabel;

        [SerializeField] private LobbyButtonGroup m_SlotButton;
        [SerializeField] private LobbyButtonGroup m_ChipButton;
        [SerializeField] private LobbyButtonGroup m_LoungeButton;

        [SerializeField] private GameObject m_ContentRoot;
        [SerializeField] private GameObject m_SlotContent;
        [SerializeField] private GameObject m_ChipContent;
        [SerializeField] private GameObject m_LoungeContent;
        [SerializeField] private RectTransform m_ScrollRect;

        [SerializeField] private Button m_HoldemButton;
        [SerializeField] private Button m_LowBadukiButton;
        [SerializeField] private Button m_BaccaratButton;
        [SerializeField] private Button m_BlackjackButton;
        [SerializeField] private Button m_LoungeHoldemButton;

        [SerializeField] private LobbyContent m_ChipGameTabContent;
        [SerializeField] private LobbyContent m_LoungeTabContent;
        [SerializeField] private AyvgameRoomList m_AyvgameRoomList;

        [SerializeField] private AnimatorMix m_AnimatorMix;
        [SerializeField] private Button m_SilverLuckyButton;
        [SerializeField] private GameObject m_SilverLuckyRemainTimeRoot;
        [SerializeField] private TextMeshProUGUI m_SilverLuckyRemainTimeLabel;
        [SerializeField] private Button m_RoyalLuckyButton;
        [SerializeField] private GameObject m_RoyalLuckyRemainTimeRoot;
        [SerializeField] private TextMeshProUGUI m_RoyalLuckyRemainTimeLabel;
        [SerializeField] private Button m_RoyalButton;
        [SerializeField] private BannerWindow m_BannerWindow;

        public enum eViewType { None, Chip, Lounge, Slot }
        private eViewType m_eViewType = eViewType.Chip;
        private bool IsFirstLobbyEnter = false;

        public eViewType ViewType
        {
            set => m_eViewType = value;

            get => m_eViewType;
        }
        private PlayCategory m_GameType;

        public PlayCategory PlayCategory
        {
            set => m_GameType = value;

            get => m_GameType;
        }

        AssetKind m_MoneyType;

        public AssetKind AssetKind
        {
            set => m_MoneyType = value;

            get => m_MoneyType;
        }


        [HideInInspector]
        public bool IsAvatarExpireCheck = false;
        [HideInInspector]
        public bool IsMemberShipExiryCheck = false;
        /// <summary>
        /// </summary>
        [HideInInspector]
        public bool IsMemberShipExpired = false;
        [HideInInspector]
        public bool IsLobbyUpdatePass = false;

        // 현재 메인 로비의 상태가 아무 탭도 안 열린 상태인지 체크
        public bool IsMainState => !m_ChipGameTabContent.gameObject.activeSelf && !m_LoungeTabContent.gameObject.activeSelf;

        public Action<TimeSpan> MemberShipTimeAction;

        private void Awake()
        {
            IsFirstLobbyEnter = false;

            m_SlotButton.OnClick = () => OnToggleChange(eViewType.Slot);
            m_ChipButton.OnClick = () => OnToggleChange(eViewType.Chip);
            m_LoungeButton.OnClick = () => OnToggleChange(eViewType.Lounge);

            m_HoldemButton.onClick.AddListener(() => OnJoinButtonClick(PlayCategory.TexasHoldem, AssetKind.Chip));
            m_LowBadukiButton.onClick.AddListener(() => OnJoinButtonClick(PlayCategory.LowBadugi, AssetKind.Chip));
            m_BaccaratButton.onClick.AddListener(() => OnJoinButtonClick(PlayCategory.Baccarat, AssetKind.Coin));
            m_BlackjackButton.onClick.AddListener(() => OnJoinButtonClick(PlayCategory.Blackjack, AssetKind.Coin));
            m_LoungeHoldemButton.onClick.AddListener(() => OnJoinButtonClick(PlayCategory.TexasHoldem, AssetKind.Coin));

            m_ChipGameTabContent.Hide();
            m_LoungeTabContent.Hide();

            m_ChipGameTabContent.action = SetGameType;
            m_LoungeTabContent.action = SetGameType;

            PM_EventManager.Subscribe(PacketID.PacketLobbyRefresh, RefreshAtriumRS);
            PM_EventManager.Subscribe(UIEvent.RefreshMoney, RefreshMoneyUI);
            PM_EventManager.Subscribe(PacketID.PacketAccessResume, RES_PacketSessionResume);
            PM_EventManager.Subscribe(PacketID.PacketReelLobbyRefresh, RefreshReelRS);
            PM_EventManager.Subscribe(PacketID.PacketConsoleStateReloadNotice, GameStateUpdateNoti);
            PM_EventManager.Subscribe(UIEvent.RefreshMemberShip, RefreshProfileMembershipDisplay);
            PM_EventManager.Subscribe(UIEvent.RefreshMemberShip, MemberShipExpiryJudge);
        }

        private void OnDestroy()
        {
            m_SlotButton.OnClick = null;
            m_ChipButton.OnClick = null;
            m_LoungeButton.OnClick = null;

            m_HoldemButton?.onClick.RemoveAllListeners();
            m_LowBadukiButton?.onClick.RemoveAllListeners();
            m_BaccaratButton?.onClick.RemoveAllListeners();
            m_BlackjackButton?.onClick.RemoveAllListeners();

            if (PM_EventManager.Instance != null)
            {
                PM_EventManager.UnSubscribe(PacketID.PacketLobbyRefresh, RefreshAtriumRS);
                PM_EventManager.UnSubscribe(PacketID.PacketReelLobbyRefresh, RefreshReelRS);
                PM_EventManager.UnSubscribe(PacketID.PacketConsoleStateReloadNotice, GameStateUpdateNoti);
                PM_EventManager.UnSubscribe(UIEvent.RefreshMoney, RefreshMoneyUI);
                PM_EventManager.UnSubscribe(PacketID.PacketAccessResume, RES_PacketSessionResume);
                PM_EventManager.UnSubscribe(UIEvent.RefreshMemberShip, RefreshProfileMembershipDisplay);
                PM_EventManager.UnSubscribe(UIEvent.RefreshMemberShip, MemberShipExpiryJudge);
            }
        }

        private async void Start()
        {
            OnToggleChange(eViewType.Chip);
            m_Membership_Standard.SetActive(false);
            m_Membership_Premium.SetActive(false);

            try
            {
                while (GaManager.Instance.GetPlayerData() == null || GaManager.Instance.Bootstrapper == null)
                {
                    await UniTask.NextFrame(destroyCancellationToken);
                }

                RefreshUI(GaManager.Instance.GetPlayerData());
            }
            catch
            {
            }
        }

        const string BGMName = "pm_lobby";
        Color m_BackColor = Color.black;
        private async void OnEnable()
        {
            RestartLuckyTicketTimer();

            if (!NtManager.Instance.IsConnect)
                return;

            if (NtManager.Instance.IsSessionReconnecting)
                return;

            IsAvatarExpireCheck = false;
            IsMemberShipExiryCheck = false;
            IsMemberShipExpired = false;

            if (PlayCategory != PlayCategory.None)
            {
                m_BackColor.a = 1;
                m_CoverImage.color = m_BackColor;
                m_CoverImage.gameObject.SetActive(true);
            }

            SendUpdate();

            BGAnimatorSetting();
            SoundPlayer.Instance.PlayBGM(BGMName);

            if (await Initialize_AYVEGameStateData())
                m_AyvgameRoomList.SlotShow(m_AYVEGameState);

            LayoutRebuilder.ForceRebuildLayoutImmediate(m_ScrollRect);
        }

        private void OnDisable()
        {
            StopLuckyTicketTimer();
            GaManager.Instance.SetAttendanceday();
        }

        private void Update()
        {
            if (IsMemberShipExiryCheck)
                MemberShipExpiryTime();
            if (IsAvatarExpireCheck)
                AvatarExpiryDateCheck();
        }

        private async UniTask LoadNoticeMessage()
        {
            try
            {
                PopupManager.Instance.ShowPopup(eUIType.Loading);

                if (!NtManager.Instance.IsConnect)
                    return;

                var pktBase = await NtManager.Instance.SendPacketAsync(PacketID.PacketBoardNotice, new BulletinIndexRQ(), Ayve.Network.LegacyIo.IoDirection.Send);

                if (pktBase == null || pktBase.ErrKind != ResultCode.ResultSuccess)
                    return;

                var res = NtManager.GetData<BulletinIndexRS>(pktBase);

                if (!(GaManager.Instance.Bootstrapper is LobbyBootstrapper))
                    return;

                var lobby = GaManager.Instance.Bootstrapper as LobbyBootstrapper;

                lobby.CheckInventoryReddot(ReddotType.Inventory_Message, res.Bulletins);

                lobby.ShowNoticePopup.Clear();
                foreach (var notice in res.Bulletins)
                {
                    if (notice.IsNoticeActive() == false)
                        continue;

                    // 한번만 체크된 것은 오늘날짜가 맞으면 넘긴다.
                    if (notice.IsOption(DisplayPolicy.SkipToday, DisplayPolicy.SkipTodayRemovable, DisplayPolicy.DailyOnce, DisplayPolicy.DailyOnceRemovable))
                    {
                        string key = BuildNoticeDismissalKey(notice.BulletinIdx, GaManager.Instance.GetPlayerIndex);
                        string dateTime = PlayerPrefs.GetString(key, string.Empty);

                        if (!string.IsNullOrEmpty(dateTime))
                        {
                            var time = TimeManager.Instance.GetParseTime(dateTime);

                            if (time.Date == DateTime.Now.Date)
                                continue;
                        }
                    }

                    // 노출옵션이 NOTICE 혹은 MSGBOX가 있는거만 팝업 띄운다.
                    if (notice.IsOption(DisplaySurface.MessageBoxAndPopup, DisplaySurface.Popup) == false)
                        continue;

                    bool isPlatform = notice.PcAvail;
#if ONE_STORE
                isPlatform = notice.OsAvail;
#elif UNITY_ANDROID
                    isPlatform = notice.PsAvail;
#elif UNITY_IOS
                isPlatform = notice.AsAvail;
#elif UNITY_STANDALONE_WIN
                    isPlatform = notice.PcAvail;
#endif
                    if (isPlatform == false)
                        continue;

                    lobby.ShowNoticePopup.Add(notice);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                PopupManager.Instance.ClosePopup(eUIType.Loading);
            }
        }

        public string BuildNoticeDismissalKey(ulong noticeIdx, ulong playerIdx)
        {
            return string.Format("NoticeDismissedAt_{0}_{1}", noticeIdx, playerIdx);
        }


        public void IsBack()
        {
            if (!IsMainState)
            {
                OnToggleChange(ViewType);
            }
            else
            {
                PopupManager.Instance.GameExitPopup();
            }
        }

        public void ShowLevelUPPopup()
        {
            if (GaManager.Instance.Level < GaManager.Instance.GetPlayerLevel)
            {
                ++GaManager.Instance.Level;
                MessagePopupData data = new MessagePopupData
                {
                    ConfirmBtnText = TextDataManager.Instance.GetString(TextDataManager.TextType.Common, 2),
                    Message = TextDataManager.Instance.GetString(TextDataManager.TextType.Popup, 166, GaManager.Instance.Level),
                    onConfirm = ShowLevelUPPopup
                };

                PopupManager.Instance.ShowPopup(eUIType.Message, data);
            }
            else
            {
                PopupManagerQueue.Instance.OnPopupClosed();
            }
        }

        private void RefreshAtriumRS(object data)
        {
            PopupManager.Instance.ShowPopup(eUIType.Loading);

            PktBase proto = (PktBase)data;

            if (proto.ErrKind == ResultCode.ResultSuccess)
            {
                var res = NtManager.GetData<RefreshAtriumRS>(proto);

                if (IsLobbyUpdatePass)
                {
                    IsLobbyUpdatePass = false;
                    PopupManager.Instance.ClosePopup(eUIType.Loading);
                    return;
                }
                else if (IsMemberShipExpired)
                {
                    GaManager.Instance.SetPlayerData(res);
                    PM_EventManager.Publish(UIEvent.RefreshMemberShip, null);
                    PM_EventManager.Publish(UIEvent.RefreshMoney, null);

                    Action afterAction = PopupManagerQueue.Instance.OnPopupClosed;
                    if (GaManager.Instance.Bootstrapper is LobbyBootstrapper lobbyBootstrapper)
                    {
                        PopupManagerQueue.Instance.Enqueue(
                       new PopupRequest(
                           PopupType.MemberShipExpire,
                           () =>
                           {
                               lobbyBootstrapper.MemberShipPopup(() => MemberShipExpiryJudge(afterAction));
                           }
                           )
                       );

                        PopupManagerQueue.Instance.TryShowNext();
                    }
                    IsMemberShipExpired = false;
                    PopupManager.Instance.ClosePopup(eUIType.Loading);
                    return;
                }

                GaManager.Instance.SetPlayerData(res);

                RefreshUI(GaManager.Instance.GetPlayerData());
                OnToggleChange(ViewType);
                OnJoinButtonClick(PlayCategory, m_MoneyType);

                m_BannerWindow.Show(res.ImgBulletins);
            }
            else
            {
                Debug.LogError($"RefreshAtriumRS Error : {proto.ErrKind}");
            }

            LobbyRefreshDisaplay();
        }

        public void LobbyRefreshDisaplay()
        {
            if (PM_BundleManager.Instance.IsLobbyScene())
            {
                if (PlayCategory != PlayCategory.None)
                    LobbyAlpha().Forget();
                else
                    PopupManagerQueueSetting();
            }
            else
                PopupManager.Instance.ClosePopup(eUIType.Loading);
        }

        /// <summary>
        /// 우선 순위에 따른 팝업 세팅.
        /// </summary>
        }

        private void RefreshReelRS(object data)
        {
            PopupManager.Instance.ShowPopup(eUIType.Loading);

            PktBase proto = (PktBase)data;

            if (proto.ErrKind == ResultCode.ResultSuccess)
            {
                var res = NtManager.GetData<RefreshReelRS>(proto);
                GaManager.Instance.SetPlayerData(res);

                RefreshUI(GaManager.Instance.GetPlayerData());
                OnToggleChange(ViewType);
                OnJoinButtonClick(PlayCategory, m_MoneyType);

                if (res.ImgBulletins.Count != 0)
                    m_BannerWindow.Show(res.ImgBulletins);
            }
            else
            {
                Debug.LogError($"RefreshReelRS Error : {proto.ErrKind}");
            }

            if (gameObject.activeInHierarchy)
                LobbyAlpha().Forget();
            else
                PopupManagerQueueSetting();
        }

        public void RefreshUI(ParticipantProfile ParticipantProfile)
        {
            m_ContentRoot.SetActive(true);

            if (ParticipantProfile == null)
                return;

            SetLuckyTicketResetTimes(ParticipantProfile);

            m_LevelLabel.text = $"Lv. {ParticipantProfile.MemberLevel}";
            RefreshProfileNickName(GaManager.Instance.GetPlayerNickName());
            m_ChipLabel.text = ParticipantProfile.WalletChips.MoneyString();
            m_CoinLabel.text = ParticipantProfile.WalletCoins.MoneyString();
            m_CashLabel.text = ParticipantProfile.GetPlayerMoney(AssetKind.Gem).MoneyString();

            RefreshAvatarIcon(ParticipantProfile);
            RefreshProfileMembershipDisplay();
        }

        public void RefreshProfileNickName(string nickName)
        {
            m_NicknameLabel.text = nickName;
        }

        public void RefreshAvatarIcon(ParticipantProfile ParticipantProfile)
        {
            if (GaManager.Instance.Bootstrapper is LobbyBootstrapper)
            {
                var lobby = GaManager.Instance.Bootstrapper as LobbyBootstrapper;
                m_AvatarIcon.sprite = lobby.GetResource<Sprite>($"Avatar{ParticipantProfile.EquippedAvatarId:D2}Circle");
            }
        }

        public void RefreshMoneyUI(object param)
        {
            ParticipantProfile ParticipantProfile = GaManager.Instance.GetPlayerData();

            m_ChipLabel.text = ParticipantProfile.WalletChips.MoneyString();
            m_CoinLabel.text = ParticipantProfile.WalletCoins.MoneyString();
            m_CashLabel.text = ParticipantProfile.GetPlayerMoney(AssetKind.Gem).MoneyString();

            RefreshRoomUI();
        }

        public void RefreshRemainLuckyTicketCoolTime(ParticipantProfile ParticipantProfile)
        {
            if (ParticipantProfile == null)
                return;

            SetLuckyTicketResetTimes(ParticipantProfile);
        }

        public void RefreshRoomUI()
        {
            if (!m_ChipGameTabContent.gameObject.activeSelf && !m_LoungeTabContent.gameObject.activeSelf)
                return;

            switch (ViewType)
            {
                case eViewType.Chip:
                    m_ChipGameTabContent.Refresh();
                    break;

                case eViewType.Lounge:
                    m_LoungeTabContent.Refresh();
                    break;
            }
        }

        public void RefreshProfileMembershipDisplay(object param = null)
        {
            var ParticipantProfile = GaManager.Instance.GetPlayerData();
            if (GaManager.Instance.IsMembershipExpired)
            {
                m_Membership_Standard.SetActive(false);
                m_Membership_Premium.SetActive(false);
            }
            else
            {
                m_Membership_Standard.SetActive(ParticipantProfile.MembershipTier == BenefitTier.Standard);
                m_Membership_Premium.SetActive(ParticipantProfile.MembershipTier == BenefitTier.Premium);
            }

        }


        private void OnToggleChange(eViewType viewType)
        {
            bool isChange = ViewType != viewType;

            ViewType = viewType;
            m_SlotContent.SetActive(viewType == eViewType.Slot);
            m_ChipContent.SetActive(viewType == eViewType.Chip);
            m_LoungeContent.SetActive(viewType == eViewType.Lounge);

            m_SlotButton.SetEnable(viewType == eViewType.Slot);
            m_ChipButton.SetEnable(viewType == eViewType.Chip);
            m_LoungeButton.SetEnable(viewType == eViewType.Lounge);

            m_LoungeTabContent.Hide();
            m_ChipGameTabContent.Hide();

            if (isChange)
            {
                BGAnimatorSetting();
            }
        }
        private void OnJoinButtonClick(PlayCategory playCategory, AssetKind assetKind = AssetKind.None)
        {
            if (m_eViewType == eViewType.Slot || (playCategory == PlayCategory.None && assetKind == AssetKind.None))
                return;

            PlayCategory = playCategory;
            AssetKind = assetKind;
            m_ContentRoot.SetActive(false);
            if (assetKind == AssetKind.Chip)
                m_ChipGameTabContent.Show(playCategory);
            else if (assetKind == AssetKind.Coin)
                m_LoungeTabContent.Show(playCategory);
        }

        private void SetGameType(PlayCategory playCategory)
        {
            switch (ViewType)
            {
                case eViewType.Chip:
                case eViewType.Lounge:
                    PlayCategory = playCategory;
                    break;
                case eViewType.Slot:
                    break;
            }
        }

        private async UniTask LobbyAlpha()
        {
            try
            {
                float elapsedTime = 0f;
                float startAlpha = 1f;

                while (elapsedTime < m_LobbyAlphaWaitSecond)
                {
                    elapsedTime += Time.deltaTime;

                    float t = Mathf.Clamp01(elapsedTime / m_LobbyAlphaWaitSecond);

                    m_BackColor.a = Mathf.SmoothStep(startAlpha, 0f, t);
                    m_CoverImage.color = m_BackColor;

                    await UniTask.NextFrame(destroyCancellationToken);
                }

                m_BackColor.a = 0f;

                if (m_CoverImage != null)
                {
                    m_CoverImage.color = m_BackColor;
                    m_CoverImage.gameObject.SetActive(false);
                }

                PopupManagerQueueSetting();
            }
            catch (OperationCanceledException)
            {
                // Destroy 등으로 취소되면 조용히 종료
            }
        }

        private void RES_PacketSessionResume(object _msg)
        {
            PktBase proto = _msg as PktBase;

            if (proto.ErrKind != ResultCode.ResultSuccess)
            {
                NtManager.Instance.ShowNetworkErrorPopup();
                return;
            }

            var reloginRes = NtManager.GetData<ResumeSigninRS>(proto);

            GaManager.Instance.SetPlayerData(reloginRes);
        }

        private void BGAnimatorSetting()
        {
            if (ViewType == eViewType.Chip)
                m_AnimatorMix.PlayAnimation("Chipgame");
            else if (ViewType == eViewType.Lounge)
                m_AnimatorMix.PlayAnimation("Lounge");
            else if (ViewType == eViewType.Slot)
                m_AnimatorMix.PlayAnimation("Slot");
        }

        private void SendUpdate()
        {
            IsAvatarExpireCheck = false;
            IsMemberShipExiryCheck = false;

            if (NtManager.Instance.serverType == NtManager.ServerType.Lobby)
            {
                var req = new RefreshAtriumRQ();

                NtManager.Instance.Dispatch(PacketID.PacketLobbyRefresh, req, Ayve.Network.LegacyIo.IoDirection.Send);
            }
            else if (NtManager.Instance.serverType == NtManager.ServerType.Slot)
            {
                var req = new RefreshReelRQ();

                NtManager.Instance.Dispatch(PacketID.PacketReelLobbyRefresh, req, Ayve.Network.LegacyIo.IoDirection.Send);
            }
        }

        /// <summary>
        /// 정보를 받았는데 현재 세팅된 아바타가 이미 만료된 경우 서버에 기본아바타로 세팅해서 보내준다.
        /// </summary>
        private void AvatarExpiryDateCheck(Action afterAction = null)
        {
            if (NtManager.Instance.serverType != NtManager.ServerType.Lobby || GaManager.Instance.CurrentAvatar == null || GaManager.Instance.CurrentAvatar.FreeAvailable)
            {
                afterAction?.Invoke();
                return;
            }

            var expiryTime = TimeManager.Instance.GetParseTime(GaManager.Instance.CurrentAvatar.ExpiresAt);
            var span = expiryTime - TimeManager.Instance.Now;
            if (span.TotalSeconds < 0)
            {
                IsAvatarExpireCheck = false;

                MemberApplySkinRQ _req = new() { SkinId = 1 };

                string avatarName = GaManager.Instance.GetAvatarData(GaManager.Instance.CurrentAvatar.AvatarRefId).Name;

                MessagePopupData data = new MessagePopupData
                {
                    ConfirmBtnText = TextDataManager.Instance.GetString(TextDataManager.TextType.Common, 2),
                    Message = TextDataManager.Instance.GetString(TextDataManager.TextType.Popup, 167, avatarName),
                    onConfirm = () => { NtManager.Instance.Dispatch(PacketID.PacketAvatarEquip, _req, Network.LegacyIo.IoDirection.Send); afterAction?.Invoke(); },
                };

                PopupManager.Instance.ShowPopup(eUIType.Message, data);
            }
            else
            {
                IsAvatarCheckJudge();
                afterAction?.Invoke();
            }
        }

        public void IsAvatarCheckJudge()
        {
            if (NtManager.Instance.serverType != NtManager.ServerType.Lobby)
            {
                IsAvatarExpireCheck = false;
                return;
            }

            IsAvatarExpireCheck = !GaManager.Instance.CurrentAvatar.FreeAvailable;
        }

        public bool MemberShipExpiryTime()
        {
            var ParticipantProfile = GaManager.Instance.GetPlayerData();

            if (ParticipantProfile == null)
                return false;
            if (GaManager.Instance.IsMembershipExpired || ParticipantProfile.MembershipTier == BenefitTier.Basic)
                return false;

            var expiryTime = TimeManager.Instance.GetParseTime(ParticipantProfile.MembershipExpiresAt);
            var timeLeft = expiryTime - TimeManager.Instance.Now;

            MemberShipTimeAction?.Invoke(timeLeft);

            if (timeLeft.TotalSeconds <= 0)
            {
                IsMemberShipExpired = true;
                GaManager.Instance.IsMembershipExpired = true;
                PM_EventManager.Publish(UIEvent.RefreshMemberShip, null);
                SendUpdate();
            }

            return true;
        }

        public void MemberShipExpiryJudge(object afterAction = null)
        {
            var ParticipantProfile = GaManager.Instance.GetPlayerData();

            if (ParticipantProfile == null)
            {
                if (afterAction != null)
                    ((Action)afterAction).Invoke();
                return;
            }

            if (GaManager.Instance.IsMembershipExpired || ParticipantProfile.MembershipTier == BenefitTier.Basic)
            {
                if (afterAction != null)
                    ((Action)afterAction).Invoke();
                return;
            }

            var expiryTime = TimeManager.Instance.GetParseTime(ParticipantProfile.MembershipExpiresAt);
            var timeLeft = expiryTime - TimeManager.Instance.Now;

            if (timeLeft.TotalSeconds <= 0)
            {
                SendUpdate();
                IsMemberShipExiryCheck = false;
            }
            else
            {
                IsMemberShipExiryCheck = true;
            }

            if (afterAction != null)
                ((Action)afterAction).Invoke();
        }

        const string AlphaAYVEGameStateDataPath = "";
        const string BetaAYVEGameStateDataPath = "";
        const string LiveAYVEGameStateDataPath = "";

        private string GetAYVEGameStateDataPath()
        {
            if (NtManager.Instance.IsReview)
            {
                if (NtManager.Instance.ReviewType == ReviewType.Alpha)
                    return AlphaAYVEGameStateDataPath;
                else
                    return BetaAYVEGameStateDataPath;
            }
#if LIVE
            return LiveAYVEGameStateDataPath;
#elif BETA
            return BetaAYVEGameStateDataPath;
#else
            return AlphaAYVEGameStateDataPath;
#endif
        }

        public Dictionary<string, LobbySlotVisibilityConfig> m_AYVEGameState = new();

        private string m_CurrentAYVEGameStateJson;
        public async UniTask<bool> Initialize_AYVEGameStateData()
        {
            bool result = false;

            string data = await RemoteDataLoader.Load<string>(GetAYVEGameStateDataPath());

            if (string.IsNullOrEmpty(data))
            {
                Debug.LogWarning("Initialize_AYVEGameStateData fail");
                return result;
            }

            bool isChanged = !string.Equals(m_CurrentAYVEGameStateJson, data, StringComparison.Ordinal);

            if (!isChanged)
                return result;

            var newState = JsonConvert.DeserializeObject<Dictionary<string, LobbySlotVisibilityConfig>>(data);

            if (newState == null)
                return result;

            m_CurrentAYVEGameStateJson = data;
            m_AYVEGameState = newState;

            result = true;
            return result;
        }

        private async void GameStateUpdateNoti(object param)
        {
            await RefreshSlotOpens();
        }

        public async UniTask RefreshSlotOpens()
        {
            if (await Initialize_AYVEGameStateData())
            {
                m_AyvgameRoomList.SlotShow(m_AYVEGameState);

                LayoutRebuilder.ForceRebuildLayoutImmediate(m_ScrollRect);
            }
        }
    }
}
