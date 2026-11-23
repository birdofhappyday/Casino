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

    public partial class LobbyMainUI
    {
        // 로비 진입 시 표시할 팝업을 우선순위 큐에 등록한다.
        private async void PopupManagerQueueSetting()
        {
            await LoadNoticeMessage();

            if (GaManager.Instance.Bootstrapper is LobbyBootstrapper lobby)
            {
                lobby.CheckMailBoxReddot();

                Action afterAction = PopupManagerQueue.Instance.OnPopupClosed;
                if (GaManager.Instance.IsAttendanceDayOff() || GaManager.Instance.IsFirstLogin || GaManager.Instance.IsInBoxRefresh)
                {
                    PopupManagerQueue.Instance.Enqueue(
                        new PopupRequest(
                            PopupType.Attendance,
                            () =>
                            {
                                PopupManager.Instance.ShowPopup(eUIType.Attendance, GaManager.Instance.GetPlayerData().AttendanceStreakDays, (Action)PopupManagerQueue.Instance.OnPopupClosed);
                            }
                            )
                        );

                    GaManager.Instance.IsFirstLogin = false;
                    GaManager.Instance.IsInBoxRefresh = false;
                    GaManager.Instance.SetAttendanceday();
                }

                if (!IsFirstLobbyEnter)
                {
                    PopupManagerQueue.Instance.Enqueue(
                        new PopupRequest(
                            PopupType.Notice,
                            () =>
                            {
                                NextNoticShow(0);
                            }
                            )
                        );
                }

                if (GaManager.Instance.IsLostLimitOver)
                {
                    PopupManagerQueue.Instance.Enqueue(
                        new PopupRequest(
                            PopupType.LimitOver,
                            () =>
                            {
                                GaManager.Instance.OpenLimitOverPopup(PopupManagerQueue.Instance.OnPopupClosed);
                            }
                            )
                        );

                    GaManager.Instance.IsLostLimitOver = false;
                }

                if (GaManager.Instance.IsGameCoinOver)
                {
                    PopupManagerQueue.Instance.Enqueue(
                        new PopupRequest(
                            PopupType.LimitOver,
                            () =>
                            {
                                lobby.MoneyLimitPopup(GameConfig.ToMoneyType(GaManager.Instance.GameCoinOverData.CapFundKind), GaManager.Instance.GameCoinOverData.InboxCheck, () =>
                                {
                                    PopupManagerQueue.Instance.OnPopupClosed();
                                });
                            }
                            )
                        );

                    GaManager.Instance.IsGameCoinOver = false;
                }

                if (GaManager.Instance.IsRefill || GaManager.Instance.IsKickOut)
                {
                    if (GaManager.Instance.IsRefill)
                    {
                        GaManager.Instance.RefillMessageData.onConfirm = PopupManagerQueue.Instance.OnPopupClosed;
                        GaManager.Instance.RefillMessageData.onClose = PopupManagerQueue.Instance.OnPopupClosed;

                        PopupManagerQueue.Instance.Enqueue(
                            new PopupRequest(
                                PopupType.Refill,
                                () =>
                                {
                                    PopupManager.Instance.ShowPopup(eUIType.Message, GaManager.Instance.RefillMessageData);
                                }
                                )
                            );
                    }
                    else
                    {
                        PopupManagerQueue.Instance.Enqueue(
                            new PopupRequest(
                                PopupType.Refill,
                                () =>
                                {
                                    MessagePopupData data = new()
                                    {
                                        ConfirmBtnText = TextDataManager.Instance.GetString(TextDataManager.TextType.Common, 2),
                                        Message = "소지 재화가 부족하여\n플레이 중이던 방에서 퇴장되었습니다.",
                                        onConfirm = PopupManagerQueue.Instance.OnPopupClosed,
                                        onClose = PopupManagerQueue.Instance.OnPopupClosed,
                                    };

                                    PopupManager.Instance.ShowPopup(eUIType.Message, data);
                                }
                                )
                            );
                    }

                    GaManager.Instance.IsRefill = false;
                    GaManager.Instance.IsKickOut = false;
                }

                if (GaManager.Instance.Level < GaManager.Instance.GetPlayerLevel)
                {
                    PopupManagerQueue.Instance.Enqueue(
                       new PopupRequest(
                           PopupType.LevelUp,
                           () =>
                           {
                               ShowLevelUPPopup();
                           }
                           )
                       );
                }

                PopupManagerQueue.Instance.Enqueue(
                    new PopupRequest(
                        PopupType.AvatarExpire,
                        () =>
                        {
                            AvatarExpiryDateCheck(afterAction);
                        }
                        )
                    );

                PopupManagerQueue.Instance.Enqueue(
                    new PopupRequest(
                        PopupType.MemberShipExpire,
                        () =>
                        {
                            lobby.MemberShipPopup(() => MemberShipExpiryJudge(afterAction));
                        }
                        )
                    );

                PopupManagerQueue.Instance.TryShowNext();
            }

            IsFirstLobbyEnter = true;

            PopupManager.Instance.ClosePopup(eUIType.Loading);
        }

        private void NextNoticShow(int page)
        {
            var lobby = GaManager.Instance.Bootstrapper as LobbyBootstrapper;

            if (lobby == null) return;

            if (lobby.ShowNoticePopup.Count == 0 || page >= lobby.ShowNoticePopup.Count)
            {
                //lobby.MemberShipPopup(() => AvatarExpiryDateCheck(ShowLevelUPPopup));
                PopupManagerQueue.Instance.OnPopupClosed();
                return;
            }

            PopupManager.Instance.ShowPopup(eUIType.LobbyNoticeItem, lobby.ShowNoticePopup[page], (Action)(() => NextNoticShow(++page)));
        }
    }
}
