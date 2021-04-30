using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

using Logger = TelegramWeatherBot.Logger;

namespace OpenWeatherApi {
    class Client {
        string token; // API key
        const string apiAddress = "http://api.openweathermap.org";
        
        public Client(string token) {
            this.token = token;
        }

        public string ForecastUrl(double lat, double lon) {
            return $"{apiAddress}/data/2.5/forecast?lat={lat}&lon={lon}&units=metric&appid={this.token}";
        }

        public Forecast5Day GetForecast5Day(HttpClient httpClient, double lat, double lon) {
            JsonSerializerOptions serializerOptions = new JsonSerializerOptions {
                IncludeFields = true
            };

            string url = ForecastUrl(lat, lon);
            try {
                var forecastTask = httpClient.GetFromJsonAsync<Forecast5Day>(url, serializerOptions);
                forecastTask.Wait(); // todo
                //Logger.LogLine($"Received OpenWeather forecast: {forecastTask.Result.cnt} elements");
                return forecastTask.Result;
            } catch (Exception e) {
                Logger.LogLine($"Exception: {e}");
                return null;
            }

            return null;
        }
    }


    public class Forecast5Day {
        public string cod { get; set; }
        public int message { get; set; }
        public int cnt { get; set; }
        public Forecast[] list { get; set; }
        //public City city { get; set; }
    }

    public class City {
        public int id { get; set; }
        public string name { get; set; }
        public Coord coord { get; set; }
        public string country { get; set; }
        public int population { get; set; }
        public int timezone { get; set; }
        public int sunrise { get; set; }
        public int sunset { get; set; }
    }

    public class Coord {
        public float lat { get; set; }
        public float lon { get; set; }
    }

    public class Forecast {
        public long dt { get; set; }
        public ForecastMain main { get; set; }
        public Weather[] weather { get; set; }
        public Clouds clouds { get; set; }
        public Wind wind { get; set; }
        public int visibility { get; set; }
        public float pop { get; set; }
        //public Sys sys { get; set; }
        public string dt_txt { get; set; }
        public Rain rain { get; set; }
    }

    public class ForecastMain {
        public float temp { get; set; }
        public float feels_like { get; set; }
        public float temp_min { get; set; }
        public float temp_max { get; set; }
        public int pressure { get; set; }
        public int sea_level { get; set; }
        public int grnd_level { get; set; }
        public int humidity { get; set; }
        public float temp_kf { get; set; }
    }

    public class Clouds {
        public int all { get; set; }
    }

    public class Wind {
        public float speed { get; set; }
        public int deg { get; set; }
        public float gust { get; set; }
    }

    
    public class Sys {
        public string pod { get; set; }
    }
    

    public class Rain {
        public float _3h { get; set; }
    }

    public class Weather {
        public int id { get; set; }
        public string main { get; set; }
        public string description { get; set; }
        public string icon { get; set; }
    }


}
