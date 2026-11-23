namespace Ayve.Managers
{
    using System;
    using System.Collections.Generic;
    using Ayve.Common;
    using Debug = Ayve.Debug;

    public enum PopupType
    {
        Attendance,         // 출석부
        Notice,             // 공지
        DailyLossLimit,     // 일손실 제한
        LimitOver,          // 한도 초과
        Refill,             // 리필
        LevelUp,            // 레벨업
        AvatarExpire,       // 아바타 만료
        MemberShipExpire,   // 멤버쉽 만료
    }

    public static class PopupPriority
    {
        public static int Get(PopupType type)
        {
            switch (type)
            {
                case PopupType.Attendance: return 10;
                case PopupType.Notice: return 20;
                case PopupType.DailyLossLimit: return 40;
                case PopupType.LimitOver: return 50;
                case PopupType.Refill: return 60;
                case PopupType.LevelUp: return 100;
                case PopupType.AvatarExpire: return 200;
                case PopupType.MemberShipExpire: return 300;
                default: return 1000;
            }
        }
    }

    public class PopupRequest
    {
        public PopupType Type;
        public int Priority;
        public DateTime CreatedAt;

        // 실제로 팝업을 보여줄 때 호출할 액션
        public Action ShowAction;

        public PopupRequest(PopupType type, Action showAction)
        {
            Type = type;
            Priority = PopupPriority.Get(type);
            CreatedAt = DateTime.UtcNow;
            ShowAction = showAction;
        }
    }

    public class PopupManagerQueue : MonoSingleton<PopupManagerQueue>
    {
        private readonly List<PopupRequest> _queue = new List<PopupRequest>();
        private PopupRequest _currentRequest = null;

        protected override void Awake()
        {
            base.Awake();
            DontDestroyOnLoad(gameObject);
        }

        public void Enqueue(PopupRequest request)
        {
            _queue.Add(request);

            // 아직 보여주지 않은 것들 정렬
            _queue.Sort((a, b) =>
            {
                int p = a.Priority.CompareTo(b.Priority);
                if (p != 0) return p;
                return a.CreatedAt.CompareTo(b.CreatedAt);
            });

            TryShowNext();
        }

        public void TryShowNext()
        {
            // 이미 하나 띄워져 있으면 대기
            if (_currentRequest != null)
                return;

            // 유효하지 않은 요청은 제거
            while (_queue.Count > 0)
            {
                _queue.RemoveAt(0);
            }

            if (_queue.Count == 0)
                return;

            // 이제 이 요청이 "현재 표시 중"인 팝업이 됨
            _currentRequest = _queue[0];
            _queue.RemoveAt(0);

            _currentRequest.ShowAction?.Invoke();
        }

        // 팝업이 닫힐 때 반드시 호출
        public void OnPopupClosed()
        {
            if (_currentRequest == null)
            {
                Debug.LogWarning("PopupManagerQueue.OnPopupClosed 호출했는데 _currentRequest가 null입니다.");
            }

            _currentRequest = null;
            TryShowNext();
        }

        public void QueueClear()
        {
            _queue.Clear();
            _currentRequest = null;
        }
    }
}
