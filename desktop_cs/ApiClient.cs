using System;
using System.Collections;
using System.Collections.Generic;
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
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

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
                using (var response = await _httpClient.GetAsync(currUrl).ConfigureAwait(false))
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
                    using (var responseHist = await _httpClient.GetAsync(histUrl).ConfigureAwait(false))
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
                double res;
                if (double.TryParse(v.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out res))
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
