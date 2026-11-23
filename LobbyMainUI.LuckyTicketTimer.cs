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
        private DateTime? m_SilverLuckyTicketResetAt;
        private DateTime? m_RoyalLuckyTicketResetAt;
        private CancellationTokenSource m_LuckyTicketTimerCancellation;

        // 럭키 티켓의 남은 시간 및 버튼 상태를 갱신한다.
        private bool SilverTicketFrame(DateTime now)
        {
            return RefreshTicketFrame(
                m_SilverLuckyButton,
                m_SilverLuckyRemainTimeRoot,
                m_SilverLuckyRemainTimeLabel,
                m_SilverLuckyTicketResetAt,
                now);
        }

        private bool RoyalTicketFrame(DateTime now)
        {
            return RefreshTicketFrame(
                m_RoyalLuckyButton,
                m_RoyalLuckyRemainTimeRoot,
                m_RoyalLuckyRemainTimeLabel,
                m_RoyalLuckyTicketResetAt,
                now);
        }

        private void SetLuckyTicketResetTimes(ParticipantProfile participantProfile)
        {
            m_SilverLuckyTicketResetAt = ParseLuckyTicketResetAt(participantProfile.ChipFreeChargeResetAt);
            m_RoyalLuckyTicketResetAt = ParseLuckyTicketResetAt(participantProfile.CoinFreeChargeResetAt);

            RestartLuckyTicketTimer();
        }

        private DateTime? ParseLuckyTicketResetAt(string resetAt)
        {
            if (string.IsNullOrEmpty(resetAt))
                return null;

            try
            {
                return TimeManager.Instance.GetParseTime(resetAt);
            }
            catch (FormatException exception)
            {
                Debug.LogError($"Lucky ticket reset time format error: {resetAt}\n{exception}");
                return null;
            }
        }

        private void RestartLuckyTicketTimer()
        {
            StopLuckyTicketTimer();

            if (!isActiveAndEnabled)
                return;

            m_LuckyTicketTimerCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            RunLuckyTicketTimerAsync(m_LuckyTicketTimerCancellation.Token).Forget();
        }

        private void StopLuckyTicketTimer()
        {
            m_LuckyTicketTimerCancellation?.Cancel();
            m_LuckyTicketTimerCancellation?.Dispose();
            m_LuckyTicketTimerCancellation = null;
        }

        private async UniTask RunLuckyTicketTimerAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = TimeManager.Instance.Now;
                bool isSilverRunning = SilverTicketFrame(now);
                bool isRoyalRunning = RoyalTicketFrame(now);

                if (!isSilverRunning && !isRoyalRunning)
                    return;

                bool isCanceled = await UniTask
                    .Delay(1000, ignoreTimeScale: true, cancellationToken: cancellationToken)
                    .SuppressCancellationThrow();

                if (isCanceled)
                    return;
            }
        }

        private bool RefreshTicketFrame(Button button, GameObject remainTimeRoot, TextMeshProUGUI remainTimeLabel, DateTime? resetAt, DateTime now)
        {
            bool isRunning = resetAt.HasValue && now <= resetAt.Value;
            bool shouldBeInteractable = !isRunning;

            if (button.interactable != shouldBeInteractable)
                button.interactable = shouldBeInteractable;

            if (remainTimeRoot.activeSelf != isRunning)
                remainTimeRoot.SetActive(isRunning);

            if (!isRunning)
                return false;

            var remainTime = resetAt.Value - now;
            remainTimeLabel.text = string.Format("{0:D2}:{1:D2}:{2:D2}", remainTime.Hours, remainTime.Minutes, remainTime.Seconds);

            return true;
        }
    }
}
