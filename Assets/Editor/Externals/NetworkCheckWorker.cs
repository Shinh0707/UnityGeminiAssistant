using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Timers = System.Timers;

namespace NetworkUtility
{
    /// <summary>
    /// NetworkCheckerのためのバックグラウンドワーカー
    /// System.Timers.Timerを使用し、動的な周期でネットワーク状態を確認します
    /// このクラスは外部アセンブリから隠蔽されています
    /// </summary>
    internal sealed class NetworkCheckWorker : IDisposable
    {
        private const double InitialCheckInterval = 10000.0;
        private const double MinCheckInterval = 2000.0;
        private const double MaxCheckInterval = 60000.0;
        private const double IntervalIncreaseFactor = 1.5;

        private static readonly HttpClient httpClient = new()
        {
            Timeout = TimeSpan.FromMilliseconds(InitialCheckInterval)
        };
        
        private readonly Timers.Timer timer;

        public NetworkCheckWorker()
        {
            timer = new Timers.Timer(InitialCheckInterval);
            timer.Elapsed += OnTimerElapsed;
            timer.AutoReset = true;
        }
        
        /// <summary>
        /// ネットワーク監視を開始します
        /// </summary>
        public void Start()
        {
            Task.Run(UpdateNetworkStateAsync);
            timer.Start();
        }

        /// <summary>
        /// タイマーを停止し、リソースを解放します
        /// </summary>
        public void Dispose()
        {
            timer.Stop();
            timer.Elapsed -= OnTimerElapsed;
            timer.Dispose();
        }

        private async void OnTimerElapsed(object sender, Timers.ElapsedEventArgs e)
        {
            await UpdateNetworkStateAsync();
            AdjustCheckInterval();
        }

        /// <summary>
        /// ネットワーク状態を非同期で確認し、結果をNetworkCheckerにセットします
        /// </summary>
        private async Task UpdateNetworkStateAsync()
        {
            try
            {
                NetworkChecker.IsNetworkAvailable = await IsInternetAvailableAsync();
            }
            catch
            {
                // バックグラウンド処理中の予期せぬ例外でアプリケーションがクラッシュすることを防ぎます
                NetworkChecker.IsNetworkAvailable = false;
            }
        }
        
        /// <summary>
        /// ネットワーク確認の周期を、プロパティへのアクセス頻度に応じて調整します
        /// </summary>
        private void AdjustCheckInterval()
        {
            int count = NetworkChecker.GetAndResetAccessCount();
            double currentInterval = timer.Interval;
            
            double newInterval = (count == 0)
                ? currentInterval * IntervalIncreaseFactor
                : currentInterval / count;

            // 新しい周期が定義された範囲内に収まるように設定します
            timer.Interval = Math.Clamp(newInterval, MinCheckInterval, MaxCheckInterval);
            
            // HttpClientのタイムアウトも、新しい周期に合わせて調整します
            var newTimeout = TimeSpan.FromMilliseconds(Math.Min(timer.Interval, MaxCheckInterval));
            if (httpClient.Timeout != newTimeout)
            {
                httpClient.Timeout = newTimeout;
            }
        }

        /// <summary>
        /// インターネットへの接続が可能かどうかを非同期で確認します
        /// </summary>
        /// <param name="uri">接続確認に使用するURInullの場合はデフォルト値が使用されます</param>
        /// <param name="cancellationToken">キャンセル要求を監視するためのトークン</param>
        /// <returns>インターネットに接続可能な場合は <c>true</c>、それ以外は <c>false</c></returns>
        public static async Task<bool> IsInternetAvailableAsync(
            Uri uri = null,
            CancellationToken cancellationToken = default)
        {
            var targetUri = uri ?? new Uri("https://www.google.com/robots.txt");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, targetUri);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                var response = await httpClient.SendAsync(request, cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }
    }
}