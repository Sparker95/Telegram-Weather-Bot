using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Text;

//using Logger = TelegramWeatherBot.Logger;

namespace TelegramWeatherBot {

    class Subscriber {
        public long id;                // Telegram user id
        public long chatId;            // Telegram chat id where to send message
        public double lat;             // Latitude for weather query
        public double lon;             // Longitude
        public uint alertHour;         // Time at which the alert will happen daily
        public uint alertMinute;
        public int timeZone;           // Time zone, amount of hours offset from UTC
        long nextAlertUnixTime; // Unix time when next alert will happen

        public Subscriber() {
            this.UpdateNextAlertTime();
        }

        public Subscriber(long id, long chatId, double lat, double lon, uint alertHour, uint alertMinute, int timeZone) {
            this.id = id;
            this.chatId = chatId;
            this.alertHour = alertHour;
            this.alertMinute = alertMinute;
            this.lat = lat;
            this.lon = lon;
            this.timeZone = timeZone;
            this.UpdateNextAlertTime();
        }

        // Changes alert time
        public void SetAlertTime(uint alertHour, uint alertMinute) {
            this.alertHour = alertHour;
            this.alertMinute = alertMinute;
            this.UpdateNextAlertTime();
        }

        // Changes city
        public void SetPos(double lat, double lon) {
            this.lat = lat;
            this.lon = lon;
        }

        // Calculates Unix time of next alert
        public void UpdateNextAlertTime() {
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            var offset = new TimeSpan(this.timeZone, 0, 0);    // UTC time when user's alert will happen
            DateTimeOffset currentAlert = new DateTimeOffset(utcNow.Year,
                        utcNow.Month,
                        utcNow.Day,
                        (int)this.alertHour,
                        (int)this.alertMinute,
                        0, // Seconds
                        offset);
            while (utcNow >= currentAlert) {
                currentAlert = currentAlert.AddDays(1); // Add one day until we're at the time of next alert
            }
            this.nextAlertUnixTime = currentAlert.ToUnixTimeSeconds();
        }

        public bool AlertHasExpired(long unixTimeNow) {
            return unixTimeNow >= this.nextAlertUnixTime;
        }

        public TimeSpan TimeUntilAlert() {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return TimeSpan.FromSeconds(this.nextAlertUnixTime - now.ToUnixTimeSeconds());
        }

        public override string ToString() {
            return $"{id} {chatId} {lat} {lon} {alertHour} {alertMinute} {timeZone}";
        }
    }

    abstract class Dialogue {
        protected long chatId;          // Telegram chat ID
        protected long userId;          // Telegram user ID
        private bool ended = false;  // If dialogue is marked as finished it will be cleared up later
        protected void EndDialogue() {
            this.ended = true;
        }

        public Dialogue() { }

        public Dialogue(long userId, long chatId) {
            this.chatId = chatId;
            this.userId = userId;
        }

        // Public getters
        public bool Ended { get { return this.ended; } }
        public long ChatId { get { return chatId; } }
        public long UserId { get { return userId; } }

        // Override to handle user messages
        // The returned string is going to be sent back to user
        public virtual TelegramApi.MethodSendMessage HandleStart() { return null;  } // Called once upon start of dialogue
        public virtual TelegramApi.MethodSendMessage HandleMessage(string msg) { return null; }
    }

    class DialogueSubscribe : Dialogue {

        public DialogueSubscribe(long userId, long chatId) : base(userId, chatId) { }

        enum State {
            waitPos,        // Waiting for user to send city name
            waitTimeZone,   // Waiting for user to send time zone
            waitAlertTime   // Waiting for user to send desired alert time
        }

        double lat, lon;
        int timeZone;
        uint alertHour, alertMinute;
        State state;

        public override TelegramApi.MethodSendMessage HandleStart() {
            var sendMsg = new TelegramApi.MethodSendMessage();
            sendMsg.text = "What are your coordinates?\nPlease provide your latitude and longitude.\nFor example: 45.67 32.312";
            state = State.waitPos;
            return sendMsg;
        }

        public override TelegramApi.MethodSendMessage HandleMessage(string msg) {
            switch (state) {
                case State.waitPos: {
                    string reply = null;
                    var parts = msg.Split(' ');
                    try {
                        this.lat = Convert.ToDouble(parts[0]);
                        this.lon = Convert.ToDouble(parts[1]);
                        if (lat <= -90.0 || lat > 90.0 || lon <= -180.0 || lon >= 180.0) {
                            throw new Exception("Coordinates are out of bounds");
                        }
                        reply = $"What is your time zone?\nFor example: 3.";
                        this.state = State.waitTimeZone;
                    } catch (Exception e) {
                        Logger.LogLine($"Error parsing coordinates: {e}");
                        reply = "Provided coordinates are incorrect. Please try again.";
                    }
                    var sendMsg = new TelegramApi.MethodSendMessage();
                    sendMsg.text = reply;
                    return sendMsg;
                    break;
                }
                case State.waitTimeZone: {
                    string reply = null;
                    try {
                        this.timeZone = Convert.ToInt32(msg);
                        if (timeZone < -12 || timeZone > 12) {
                            throw new Exception("Time zone is out of bounds");
                        }
                        reply = $"What is the local time when you want to receive your forecasts?\nFor example: 9:30.";
                        this.state = State.waitAlertTime;
                    } catch (Exception e) {
                        Logger.LogLine($"Error parsing time zone: {timeZone}");
                        Logger.LogLine($"{e}");
                        reply = "Provided time zone is incorrect. Please try again.";
                    }
                    var sendMsg = new TelegramApi.MethodSendMessage();
                    sendMsg.text = reply;
                    return sendMsg;
                    break;
                }
                case State.waitAlertTime: {
                    string reply = null;
                    var parts = msg.Split(':', ' ');
                    try {
                        this.alertHour = Convert.ToUInt32(parts[0]);
                        this.alertMinute = Convert.ToUInt32(parts[1]);
                        if (alertHour >= 24 || alertMinute >= 60) {
                            throw new Exception("Time format is incorrect");
                        }
                        Subscriber sub = new Subscriber(
                            this.userId,
                            this.chatId,
                            this.lat, this.lon,
                            this.alertHour, this.alertMinute,
                            this.timeZone);
                        Program.AddSubscriber(sub);
                        TimeSpan timeUntilAlert = sub.TimeUntilAlert();
                        int hours = timeUntilAlert.Hours;
                        int minutes = timeUntilAlert.Minutes;
                        StringBuilder sb = new StringBuilder(128);
                        sb.AppendLine($"Your coordinates are: {lat} {lon}");
                        sb.AppendLine($"Your time zone is: {timeZone}");
                        sb.AppendLine($"You will receive daily updates at {this.alertHour}:{this.alertMinute}");
                        sb.AppendLine($"Your next update will be in {hours} hours {minutes} minutes");
                        sb.AppendLine($"Have a nice day!");
                        reply = sb.ToString();
                        this.EndDialogue();
                    } catch(Exception e) {
                        Logger.LogLine($"Error parsing time: {e}");
                        reply = "Provided time is incorrect. Please try again.";
                    }
                    var sendMsg = new TelegramApi.MethodSendMessage();
                    sendMsg.text = reply;
                    return sendMsg;
                    break;
                }
            }

            return null;
        }
    }

    class DialogueChangeAlertTime {
        // Just one state, waiting for user to provide time
    }

    class Program {

        static int verMajor = 1;
        static int verMinor = 2;

        static HttpClient httpClient;
        static TelegramApi.Bot bot;
        static OpenWeatherApi.Client openWeather;
        static JsonSerializerOptions serializerOptions;

        static Dictionary<long, Subscriber> subscribers = null;
        static Dictionary<long, Dialogue> dialogues = new Dictionary<long, Dialogue>();

        static void Main(string[] args) {

            Logger.Init();  // Init file for logging

            Logger.LogLine($"TelegramWeatherBot version {verMajor}.{verMinor}");

            serializerOptions = new JsonSerializerOptions {
                IncludeFields = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };


            // Read keys from the file
            string botToken, botName, openWeatherToken;
            try {
                Logger.Log("Reading keys.cfg ... ");
                var cfgFileLines = File.ReadAllLines("keys.cfg");
                botName = cfgFileLines[0];
                botToken = cfgFileLines[1];
                openWeatherToken = cfgFileLines[2];
                Logger.LogLine("ok", false);
            }
            catch (Exception e) {
                Logger.LogLine($"Error reading keys.cfg: {e}", false);
                return;
            }

            // Read subscribers from the file
            Logger.Log("Reading subscribers.json ... ");
            LoadSubscribers();
            Logger.LogLine("ok", false);

            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10.0f);
            bot = new TelegramApi.Bot(botName, botToken);
            openWeather = new OpenWeatherApi.Client(openWeatherToken);

            Logger.Log("Testing Telegram API connection ... ");
            TelegramApi.User botUser = bot.GetMe(httpClient);
            if (botUser == null) {
                Logger.LogLine("Telegram API error!", false);
                return;
            } else {
                Logger.LogLine("ok", false);
                Logger.LogLine("Bot user data:");
                Logger.LogLine($"  id: {botUser.id}");
                Logger.LogLine($"  username: {botUser.username}");
                Logger.LogLine($"  first_name: {botUser.first_name}");
                Logger.LogLine($"  last_name: {botUser.last_name}");
            }

            Logger.Log("Testing OpenWeather connection ... ");
            OpenWeatherApi.Forecast5Day forecast = openWeather.GetForecast5Day(httpClient, 55.7558, 37.6173);
            if (forecast == null) {
                Logger.LogLine("OpenWeather API error!", false);
            } else {
                Logger.LogLine("ok", false);
                Logger.LogLine("Forecast:");
                Logger.LogLine($"  temp: {forecast.list[0].main.temp} deg");
            }

            Logger.LogLine("Hello from weather telegram bot!");

            while (true) {
                GetUpdates();
                CheckSubscriberAlerts();
                Thread.Sleep(100);
            }

        }

        static void GetUpdates() {

            TelegramApi.Update[] updates = bot.GetUpdates(httpClient);

            if (updates != null) {
                if (updates.Length > 0) {
                    Logger.LogLine($"Received {updates.Length} updates:");
                    foreach (TelegramApi.Update update in updates) {
                        HandleUpdate(update);
                    }
                }
            } else {
                Logger.LogLine("Error getting updates");
            }
        }

        static void HandleUpdate(TelegramApi.Update update) {
            //
            Logger.LogLine($"HandleUpdate: {update.update_id}");
            if (update.message != null) {
                if (update.message.text != null) {
                    Logger.LogLine($"Received text from {update.message.from.id} {update.message.from.username} :\n{update.message.text}");

                    // Check what the message text was
                    string msgCmd = bot.GetCommand(update.message.text);
                    if (msgCmd != null) {

                        // If we have received a command during a dialogue
                        // then we must terminate the current dialogue
                        if (dialogues.ContainsKey(update.message.chat.id)) {
                            dialogues.Remove(update.message.chat.id);
                        }

                        if (msgCmd.Equals("/start")) {
                            bot.SendPlainMessage(httpClient, update.message.chat.id, "Hi from the weather bot! Please use one of the provided commands.");

                        } else if (msgCmd.Equals("/subscribe")) {
                            var chatId = update.message.chat.id;
                            Dialogue dialogue = new DialogueSubscribe(update.message.from.id, update.message.chat.id);
                            InitDialogue(dialogue);

                        } else if (msgCmd.Equals("/unsubscribe")) {
                            RemoveSubscriber(update.message.from.id);
                            bot.SendPlainMessage(httpClient, update.message.chat.id, "You have unsubscribed from the bot.");

                        } else if (msgCmd.Equals("/forecast")) {
                            Subscriber sub;
                            subscribers.TryGetValue(update.message.from.id, out sub);
                            if (sub != null) {
                                SendForecastTo(sub);
                            } else {
                                bot.SendPlainMessage(httpClient, update.message.chat.id, "You need to /subscribe first!");
                            }
                        } else if (msgCmd.Equals("/info")) {
                            Subscriber sub;
                            subscribers.TryGetValue(update.message.from.id, out sub);
                            string reply = null;
                            if (sub != null) {
                                StringBuilder sb = new StringBuilder(128);
                                TimeSpan timeUntilAlert = sub.TimeUntilAlert();
                                int hours = timeUntilAlert.Hours;
                                int minutes = timeUntilAlert.Minutes;
                                sb.AppendLine($"Your coordinates are: {sub.lat} {sub.lon}");
                                sb.AppendLine($"Your time zone is: {sub.timeZone}");
                                sb.AppendLine($"You will receive daily updates at {sub.alertHour}:{sub.alertMinute}");
                                sb.AppendLine($"Your next update will be in {hours} hours {minutes} minutes");
                                reply = sb.ToString();
                            } else {
                                reply = "You are not subscribed to this bot!";
                            }
                            bot.SendPlainMessage(httpClient, update.message.from.id, reply);
                        }
                    } else {
                        // Received plain text, not a command
                        // Check if it belongs to a dialogue
                        Dialogue dialogue;
                        dialogues.TryGetValue(update.message.chat.id, out dialogue);
                        if (dialogue != null) {
                            var sendMsgBack = dialogue.HandleMessage(update.message.text);
                            if (sendMsgBack != null) {  // Send msg back to user
                                sendMsgBack.chat_id = dialogue.ChatId;
                                bot.PostJsonSync(httpClient, "sendMessage", sendMsgBack);
                            }
                            if (dialogue.Ended) {
                                dialogues.Remove(update.message.chat.id);
                            }
                        } else {
                            bot.SendPlainMessage(httpClient, update.message.chat.id, "What's going on??");
                        }
                    }
                }
            }
        }

        static void SendForecastTo(Subscriber sub) {
            OpenWeatherApi.Forecast5Day forecast5 = openWeather.GetForecast5Day(httpClient, sub.lat, sub.lon);
            StringBuilder s = new StringBuilder(512);
            s.Append($"Forecast for: {sub.lat} {sub.lon}\n\n");
            for (uint i = 0; i < 8; i++) {
                s.Append(FormatForecast(forecast5.list[i], sub.timeZone));
                s.Append("\n");
            }
            s.Append("Data provided by https://openweathermap.org");
            bot.SendPlainMessage(httpClient, sub.chatId, s.ToString(), true);
            Logger.LogLine($"Sent forecast to {sub.id}");
        }

        static string FormatForecast(OpenWeatherApi.Forecast f, int timeZone) {
            StringBuilder s = new StringBuilder(128);
            DateTime d = DateTimeOffset.FromUnixTimeSeconds(f.dt).DateTime;
            d.AddHours((double)timeZone);
            d.AddHours(timeZone);
            s.Append($"Date:    {d}\n");
            s.Append($"Temp:    {(int)Math.Round(f.main.temp)} deg.c\n");
            s.Append($"Wind:    {(int)Math.Round(f.wind.speed)} m/s, Gusts: {(int)Math.Round(f.wind.gust)} m/s\n");
            s.Append($"Weather: {f.weather[0].main} - {f.weather[0].description}\n");
            s.Append($"POP:     {(int)f.pop*100.0f} %\n");
            return s.ToString();
        }

        static void InitDialogue(Dialogue dialogue) {
            // Run start code
            var sendMsg = dialogue.HandleStart();
            
            // Send message back to user if requested
            if (sendMsg != null) {
                sendMsg.chat_id = dialogue.ChatId;
                bot.PostJsonSync(httpClient, "sendMessage", sendMsg);
            }

            // Add to the list of dialogues
            dialogues.Add(dialogue.ChatId, dialogue);
        }

        static void CheckSubscriberAlerts() {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            long unixTime = now.ToUnixTimeSeconds();
            foreach (KeyValuePair<long, Subscriber> p in subscribers) {
                Subscriber sub = p.Value;
                if (sub.AlertHasExpired(unixTime)) {
                    SendForecastTo(sub);
                    sub.UpdateNextAlertTime();
                }
            }
        }

        public static void AddSubscriber(Subscriber sub) {
            RemoveSubscriber(sub.id);   // Remove subscriber if he already exists
            subscribers.Add(sub.id, sub);
            SaveSubscribers();
            Logger.LogLine($"Added subscriber: {sub}");
        }

        public static void RemoveSubscriber(long userId) {
            if (subscribers.ContainsKey(userId)) {
                subscribers.Remove(userId);
                SaveSubscribers();
                Logger.LogLine($"Removed subscriber: {userId}");
            }
        }

        static void SaveSubscribers() {
            JsonSerializerOptions opts = new JsonSerializerOptions {
                IncludeFields = true,
                WriteIndented = true
            };

            string subSerialized = JsonSerializer.Serialize<Dictionary<long, Subscriber>>(subscribers, opts);
            File.WriteAllText("subscribers.json", subSerialized);
        }

        static void LoadSubscribers() {
            JsonSerializerOptions opts = new JsonSerializerOptions {
                IncludeFields = true
            };

            try {
                string subSerialized = File.ReadAllText("subscribers.json");
                subscribers = JsonSerializer.Deserialize<Dictionary<long, Subscriber>>(subSerialized, opts);
            }
            catch (FileNotFoundException e) {
                subscribers = new Dictionary<long, Subscriber>();
            }
        }


    }
}
