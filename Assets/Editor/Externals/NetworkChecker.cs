using System;
using System.Threading;

namespace NetworkUtility
{
    /// <summary>
    /// アプリケーション全体のネットワーク接続状態をバックグラウンドで監視し、提供します。
    /// 監視周期は、このクラスのプロパティへのアクセス頻度に応じて動的に調整されます。
    /// </summary>
    public static class NetworkChecker
    {
        private const int AccessThresholdMilliseconds = 500;

        private static readonly NetworkCheckWorker worker;
        private static readonly object accessLock = new object();

        private static bool isNetworkAvailable;
        private static int accessCount;
        private static DateTime lastAccessTime = DateTime.MinValue;

        /// <summary>
        /// ネットワーク接続が利用可能かどうかを示す最新の状態を取得します。
        /// このプロパティへのアクセスは、短時間の連続呼び出しが集約された上で、
        /// 動的な監視周期の計算に使用されます。
        /// </summary>
        public static bool IsNetworkAvailable
        {
            get
            {
                lock (accessLock)
                {
                    var now = DateTime.UtcNow;
                    if ((now - lastAccessTime).TotalMilliseconds > AccessThresholdMilliseconds)
                    {
                        accessCount++;
                        lastAccessTime = now;
                    }
                }
                return isNetworkAvailable;
            }
            internal set => isNetworkAvailable = value;
        }

        /// <summary>
        /// NetworkCheckerクラスが初めてアクセスされたときに一度だけ呼び出される静的コンストラクタです。
        /// </summary>
        static NetworkChecker()
        {
            isNetworkAvailable = false;
            worker = new NetworkCheckWorker();
            worker.Start();
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        /// <summary>
        /// 現在のアクセス数を取得し、アトミックに0にリセットします。
        /// </summary>
        /// <returns>リセットされる前のアクセス数。</returns>
        internal static int GetAndResetAccessCount()
        {
            lock (accessLock)
            {
                int currentCount = accessCount;
                accessCount = 0;
                return currentCount;
            }
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            worker.Dispose();
        }
    }
}