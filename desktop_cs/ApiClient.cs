using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace XDripWidget
{
    public class FetchResult
    {
        public CurrentGlucoseData CurrentData { get; set; }
        public List<HistoryPoint> History { get; set; }
        public string ErrorMessage { get; set; }

        public FetchResult()
        {
            History = new List<HistoryPoint>();
        }

        public bool IsSuccess
        {
            get { return string.IsNullOrEmpty(ErrorMessage) && CurrentData != null; }
        }
    }

    public class ApiClient
    {
        private static readonly HttpClient _httpClient;

        static ApiClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 |
                    SecurityProtocolType.Tls11 |
                    SecurityProtocolType.Tls;
                ServicePointManager.DefaultConnectionLimit = 20;
            }
            catch { }

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
        }

        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        private async Task<HttpResponseMessage> SendWithRetryAsync(HttpMethod method, string url, Func<HttpContent> contentFactory = null)
        {
            Exception lastEx = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool shouldRetry = false;
                try
                {
                    var request = new HttpRequestMessage(method, url);
                    request.Headers.ConnectionClose = true;
                    if (contentFactory != null)
                    {
                        request.Content = contentFactory();
                    }
                    return await _httpClient.SendAsync(request).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    lastEx = ex;
                    if (attempt == 0) shouldRetry = true;
                }
                catch (TaskCanceledException)
                {
                    // Do not retry on genuine client timeout to avoid doubling delay
                    throw;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    if (attempt == 0) shouldRetry = true;
                }

                if (shouldRetry)
                {
                    await Task.Delay(350).ConfigureAwait(false);
                }
            }

            if (lastEx != null)
            {
                throw lastEx;
            }
            throw new Exception("Не удалось выполнить сетевой запрос");
        }

        public async Task<FetchResult> FetchAllAsync(string baseUrl, string apiSecret)
        {
            var result = new FetchResult();
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                result.ErrorMessage = "Адрес сервера не указан";
                return result;
            }

            string cleanUrl = baseUrl.Trim().TrimEnd('/');
            string tokenParam = string.IsNullOrWhiteSpace(apiSecret) ? "" : string.Format("?token={0}", Uri.EscapeDataString(apiSecret.Trim()));
            string tokenParamHist = string.IsNullOrWhiteSpace(apiSecret) ? "?hours=4" : string.Format("?hours=4&token={0}", Uri.EscapeDataString(apiSecret.Trim()));

            string currUrl = string.Format("{0}/api/v1/current{1}", cleanUrl, tokenParam);
            string histUrl = string.Format("{0}/api/v1/history{1}", cleanUrl, tokenParamHist);

            try
            {
                // 1. Fetch current data
                using (var response = await SendWithRetryAsync(HttpMethod.Get, currUrl).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        result.ErrorMessage = string.Format("Ошибка сервера (HTTP {0})", (int)response.StatusCode);
                        return result;
                    }

                    string jsonStr = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var dict = _serializer.Deserialize<Dictionary<string, object>>(jsonStr);
                    if (dict != null)
                    {
                        result.CurrentData = ParseCurrentData(dict);
                    }
                    else
                    {
                        result.ErrorMessage = "Неверный формат ответа";
                        return result;
                    }
                }

                // 2. Fetch history (4 hours)
                try
                {
                    using (var responseHist = await SendWithRetryAsync(HttpMethod.Get, histUrl).ConfigureAwait(false))
                    {
                        if (responseHist.IsSuccessStatusCode)
                        {
                            string jsonHistStr = await responseHist.Content.ReadAsStringAsync().ConfigureAwait(false);
                            var list = _serializer.Deserialize<ArrayList>(jsonHistStr);
                            if (list != null)
                            {
                                foreach (var item in list)
                                {
                                    var hDict = item as Dictionary<string, object>;
                                    if (hDict != null)
                                    {
                                        double mmol = SafeDouble(hDict, "mmol", -1);
                                        long ts = SafeLong(hDict, "timestamp", 0);
                                        if (mmol > 0 && ts > 0)
                                        {
                                            result.History.Add(new HistoryPoint { Timestamp = ts, Mmol = mmol });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // History failure is non-fatal
                }
            }
            catch (TaskCanceledException)
            {
                result.ErrorMessage = "Таймаут соединения";
            }
            catch (HttpRequestException ex)
            {
                result.ErrorMessage = FormatNetworkError(ex);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = string.Format("Ошибка: {0}", ex.Message);
            }

            return result;
        }

        public async Task<bool> SubmitTreatmentAsync(string baseUrl, string apiSecret, string eventType, double carbs, double insulin, double glucose, string notes, DateTime date)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return false;
            string cleanUrl = baseUrl.Trim().TrimEnd('/');
            string tokenParam = string.IsNullOrWhiteSpace(apiSecret) ? "" : string.Format("?token={0}", Uri.EscapeDataString(apiSecret.Trim()));
            string url = string.Format("{0}/api/v1/treatments{1}", cleanUrl, tokenParam);

            var dict = new Dictionary<string, object>
            {
                { "uuid", Guid.NewGuid().ToString() },
                { "_id", Guid.NewGuid().ToString() },
                { "eventType", eventType },
                { "carbs", carbs },
                { "insulin", insulin },
                { "glucose", glucose },
                { "notes", notes ?? "" },
                { "created_at", date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") },
                { "date", (long)(date.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds }
            };

            string json = _serializer.Serialize(dict);
            try
            {
                using (var response = await SendWithRetryAsync(HttpMethod.Post, url, () => new StringContent(json, System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false))
                {
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<TreatmentItem>> GetTreatmentsAsync(string baseUrl, string apiSecret, int count = 50)
        {
            var list = new List<TreatmentItem>();
            if (string.IsNullOrWhiteSpace(baseUrl)) return list;
            string cleanUrl = baseUrl.Trim().TrimEnd('/');
            string tokenParam = string.IsNullOrWhiteSpace(apiSecret) ? string.Format("?count={0}", count) : string.Format("?count={0}&token={1}", count, Uri.EscapeDataString(apiSecret.Trim()));
            string url = string.Format("{0}/api/v1/treatments{1}", cleanUrl, tokenParam);

            using (var response = await SendWithRetryAsync(HttpMethod.Get, url).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) return list;

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var rawList = _serializer.Deserialize<ArrayList>(json);
                if (rawList == null) return list;

                foreach (var item in rawList)
                {
                    var d = item as Dictionary<string, object>;
                    if (d == null) continue;

                    var t = new TreatmentItem
                    {
                        Id = SafeString(d, "_id", SafeString(d, "uuid", "")),
                        EventType = SafeString(d, "eventType", "Treatment"),
                        Carbs = SafeDouble(d, "carbs", 0),
                        Insulin = SafeDouble(d, "insulin", 0),
                        Glucose = SafeDouble(d, "glucose", 0),
                        Notes = SafeString(d, "notes", "")
                    };

                    long ms = SafeLong(d, "date", 0);
                    if (ms > 0)
                    {
                        t.Date = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime();
                    }
                    else
                    {
                        string ca = SafeString(d, "created_at", "");
                        DateTime dt;
                        if (DateTime.TryParse(ca, out dt)) t.Date = dt.ToLocalTime();
                        else t.Date = DateTime.Now;
                    }

                    list.Add(t);
                }
                return list;
            }
        }

        public async Task<bool> DeleteTreatmentAsync(string baseUrl, string apiSecret, string id)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(id)) return false;
            string cleanUrl = baseUrl.Trim().TrimEnd('/');
            string tokenParam = string.IsNullOrWhiteSpace(apiSecret) ? "" : string.Format("?token={0}", Uri.EscapeDataString(apiSecret.Trim()));
            string url = string.Format("{0}/api/v1/treatments/{1}{2}", cleanUrl, id, tokenParam);

            try
            {
                using (var response = await SendWithRetryAsync(HttpMethod.Delete, url).ConfigureAwait(false))
                {
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private CurrentGlucoseData ParseCurrentData(Dictionary<string, object> d)
        {
            var data = new CurrentGlucoseData
            {
                Mmol = SafeDouble(d, "mmol", 0.0),
                Direction = SafeString(d, "direction", "Unknown"),
                Delta = SafeString(d, "delta", "?"),
                Battery = SafeInt(d, "battery", -1),
                MinutesAgo = SafeInt(d, "minutes_ago", 0),
                Timestamp = SafeLong(d, "timestamp", 0)
            };
            return data;
        }

        private static double SafeDouble(Dictionary<string, object> d, string key, double defVal)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                if (v is double) return (double)v;
                if (v is float) return (double)(float)v;
                if (v is decimal) return (double)(decimal)v;
                if (v is int) return (double)(int)v;
                if (v is long) return (double)(long)v;

                string s = v.ToString().Trim().Replace(',', '.');
                double res;
                if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out res))
                {
                    return res;
                }
            }
            return defVal;
        }

        private static int SafeInt(Dictionary<string, object> d, string key, int defVal)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                if (v is int) return (int)v;
                if (v is long) return (int)(long)v;
                if (v is double) return (int)(double)v;
                if (v is decimal) return (int)(decimal)v;
                int res;
                if (int.TryParse(v.ToString(), out res)) return res;
            }
            return defVal;
        }

        private static long SafeLong(Dictionary<string, object> d, string key, long defVal)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                if (v is long) return (long)v;
                if (v is int) return (int)v;
                if (v is double) return (long)(double)v;
                if (v is decimal) return (long)(decimal)v;
                long res;
                if (long.TryParse(v.ToString(), out res)) return res;
            }
            return defVal;
        }

        private static string SafeString(Dictionary<string, object> d, string key, string defVal)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                string s = v.ToString();
                return string.IsNullOrEmpty(s) ? defVal : s;
            }
            return defVal;
        }

        private static string FormatNetworkError(HttpRequestException ex)
        {
            string msg = ex.ToString().ToLower();
            if (msg.Contains("11001") || msg.Contains("getaddrinfo")) return "Сервер не найден";
            if (msg.Contains("10061") || msg.Contains("refused")) return "Сервер недоступен";
            if (msg.Contains("10054") || msg.Contains("reset")) return "Соединение разорвано";
            return "Нет связи с сервером";
        }
    }
}
