using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Threading;

namespace TelegramWeatherBot {

    class Subscriber {
        long id;            // Telegram user id
        long chatId;        // Telegram chat id where to send message
        double lat;          // Latitude for weather query
        double lon;          // Longitude
        uint alertHour;     // Time at which the alert will happen daily
        uint alertMinute;
        long nextAlertUnixTime; // Unix time when next alert will happen

        public Subscriber(long id, long chatId, double lat, double lon, uint alertHour, uint alertMinute) {
            this.id = id;
            this.chatId = chatId;
            this.alertHour = alertHour;
            this.alertMinute = alertMinute;
            this.lat = lat;
            this.lon = lon;
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
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset currentAlert = new DateTimeOffset(now.Year, now.Month, now.Day, (int)this.alertHour, (int)this.alertMinute, 0, new TimeSpan(0));
            while (now >= currentAlert) {
                currentAlert = currentAlert.AddDays(1); // Add one day until we're at the time of next alert
            }
            this.nextAlertUnixTime = currentAlert.ToUnixTimeSeconds();
        }

        public bool AlertHasExpired(long unitTimeNow) {
            return unitTimeNow >= this.nextAlertUnixTime;
        }
    }

    abstract class Dialogue {
        protected long userId;          // Telegram ID of the user
        private bool ended = false;  // If dialogue is marked as finished it will be cleared up later
        protected void EndDialogue() {
            this.ended = true;
        }

        public Dialogue() { }

        public Dialogue(long userId) {
            this.userId = userId;
        }

        // Public getters
        public bool Ended { get { return this.ended; } }
        public long UserId { get { return userId;  } }

        // Override to handle user messages
        // The returned string is going to be sent back to user
        public virtual TelegramApi.MethodSendMessage HandleStart() { return null;  } // Called once upon start of dialogue
        public virtual TelegramApi.MethodSendMessage HandleMessage(string msg) { return null; }
    }

    class DialogueSubscribe : Dialogue {

        enum State {
            waitPos,       // Waiting for user to send city name
            waitAlertTime   // Waiting for user to send desired alert time
        }

        double lat, lon;
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
                        lat = Convert.ToDouble(parts[0]);
                        lon = Convert.ToDouble(parts[1]);
                        if (lat <= -90.0 || lat > 90.0 || lon <= -180.0 || lon >= 180.0) {
                            throw new Exception("Coordinates are out of bounds");
                        }
                        reply = $"Your coordinates are: {lat} {lon}\nPlease provide the time at which you want to receive the forecast in format HH:MM.\nFor example: 9:30";
                    }
                    catch (Exception e) {
                        Console.WriteLine($"Error parsing coordinates: {e}");
                        reply = "Your coordinates have wrong format. Please try again.";
                    }
                    var sendMsg = new TelegramApi.MethodSendMessage();
                    sendMsg.text = reply;
                    return sendMsg;
                    break;
                }
                case State.waitAlertTime: {
                    break;
                }
            }
        }
    }

    class DialogueChangeAlertTime {
        // Just one state, waiting for user to provide time
    }

    class Program {

        // todo: move this to external file
        public const string botName = "SparkTest95Bot";
        public const string botToken = "1711289807:AAGMNXL6dtCSAG00cCRC9wK05mWYi4-y3ZM";
        public const string openWeatherToken = "84ca398bfecc5c94cb14466d9172ed54";

        static HttpClient httpClient;
        static TelegramApi.Bot bot;
        static OpenWeatherApi.Client openWeather;
        static JsonSerializerOptions serializerOptions;

        static List<long> subscribers = new List<long>();
        static Dictionary<long, Dialogue> dialogues = new Dictionary<long, Dialogue>();

        static void Main(string[] args) {

            /*
            var test0 = TelegramApi.GetCommand("/start");
            var test1 = TelegramApi.GetCommand("/end");
            var test2 = TelegramApi.GetCommand("whatever");
            var test3 = TelegramApi.GetCommand("/doStuff@BotName");
            var test4 = TelegramApi.GetCommand("/doStuff@SparkTest95Bot");
            */

            /*
            Subscriber sub = new Subscriber(0, 0, 22, 30);
            sub.UpdateNextAlertTime();
            DateTimeOffset nextAlertTime = DateTimeOffset.FromUnixTimeSeconds(sub.timeNextAlert);
            Console.WriteLine($"Current UTC time: {DateTimeOffset.UtcNow}");
            Console.WriteLine($"Next UTC alert time: {nextAlertTime}");

            return;
            */

            serializerOptions = new JsonSerializerOptions {
                IncludeFields = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            TelegramApi.MethodSendMessage msgTest = new TelegramApi.MethodSendMessage();
            msgTest.text = "123";
            //msgTest.message = new TelegramApi.Message();
            string msgTestSer = JsonSerializer.Serialize<TelegramApi.MethodSendMessage>(msgTest, serializerOptions);
            Console.WriteLine(msgTestSer);

            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10.0f);
            bot = new TelegramApi.Bot(botName, botToken);
            openWeather = new OpenWeatherApi.Client(openWeatherToken);



            Console.WriteLine("Hello from telegram bot test!");

            while (true) {
                GetUpdates();
                Thread.Sleep(500);
            }

        }

        static void GetUpdates() {

            TelegramApi.Update[] updates = bot.GetUpdates(httpClient);

            if (updates != null) {
                Console.WriteLine($"Received {updates.Length} updates:");
                foreach (TelegramApi.Update update in updates) {
                    HandleUpdate(update);
                }
            } else {
                Console.WriteLine("Error getting updates");
            }
        }

        static void HandleUpdate(TelegramApi.Update update) {
            //
            Console.WriteLine($"HandleUpdate: {update.update_id}");
            if (update.message != null) {
                if (update.message.text != null) {
                    Console.WriteLine($"  Received msg: {update.message.text}");

                    // Check what the message text was
                    string msgCmd = bot.GetCommand(update.message.text);
                    if (msgCmd != null) {
                        if (msgCmd.Equals("/start")) {
                            long userId = update.message.chat.id;
                            string userName = update.message.from.username;
                            string textReply;

                            if (subscribers.Contains(userId)) {
                                textReply = "You are already subscribed to this bot";
                            } else {
                                textReply = "You have just subscribed to this bot";
                                Console.WriteLine($"User {userName} {userId} has subscribed");
                            }

                            var args = new TelegramApi.MethodSendMessage {
                                chat_id = update.message.chat.id,
                                text = textReply
                            };
                            args.reply_markup = new TelegramApi.ReplyKeyboardRemove();
                            var httpContent = JsonContent.Create<TelegramApi.MethodSendMessage>(args,
                                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"),
                                serializerOptions);
                            var task = httpClient.PostAsync(bot.MethodUrl("sendMessage"), httpContent);
                            task.Wait();

                            subscribers.Add(update.message.chat.id);
                        } else if (msgCmd.Equals("/test")) {
                            var sendMsg = new TelegramApi.MethodSendMessage();
                            sendMsg.chat_id = update.message.chat.id;
                            sendMsg.text = "Hello hello hello";
                            var mkup = new TelegramApi.ReplyKeyboardMarkup();
                            var buttonLocation = new TelegramApi.KeyboardButton();
                            buttonLocation.request_location = true;
                            buttonLocation.text = "Where are you?";
                            var buttonTest = new TelegramApi.KeyboardButton();
                            buttonTest.text = "Test button";
                            mkup.keyboard = new TelegramApi.KeyboardButton[2][];
                            mkup.keyboard[0] = new TelegramApi.KeyboardButton[1];
                            mkup.keyboard[1] = new TelegramApi.KeyboardButton[1];
                            mkup.keyboard[0][0] = buttonLocation;
                            mkup.keyboard[1][0] = buttonTest;
                            sendMsg.reply_markup = mkup;
                            var task = httpClient.PostAsync(
                                bot.MethodUrl("sendMessage"),
                                JsonContent.Create<TelegramApi.MethodSendMessage>(
                                    sendMsg,
                                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"),
                                    serializerOptions)
                                );
                            task.Wait();
                            Console.WriteLine("Sent keyboard!");
                        }
                    }
                }
            }
        }

        static void InitDialogue(Dialogue dialogue) {
            // Run start code
            var sendMsg = dialogue.HandleStart();
            
            // Send message back to user if requested
            if (sendMsg != null) {

            }

            // Add to the list of dialogues
            dialogues.Add(dialogue.UserId, dialogue);
        }
    }
}
