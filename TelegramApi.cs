using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TelegramApi {

    class Bot {
        string name;
        string token;
        const string apiAddress = "api.telegram.org";
        public const uint getUpdatesTimeout = 2;
        int nextUpdateId = -1;

        public Bot(string name, string token) {
            this.name = name;
            this.token = token;
        }

        public string MethodUrl(string methodName, string parameters = null) {
            if (parameters == null)
                return $"https://{apiAddress}/bot{this.token}/{methodName}";
            else
                return $"https://{apiAddress}/bot{this.token}/{methodName}?{parameters}";
        }

        public bool MentionsMe(string msg) {
            return msg.Contains($"@{this.name}");
        }

        public Update[] GetUpdates(HttpClient httpClient) {
            JsonSerializerOptions serializerOptions = new JsonSerializerOptions {
                IncludeFields = true
            };
            string getUpdatesParams = nextUpdateId != -1 ?
                $"offset={nextUpdateId}&timeout={getUpdatesTimeout}" :
                $"timeout={getUpdatesTimeout}";
            string url = this.MethodUrl("getUpdates", getUpdatesParams);

            try {
                var getTask = httpClient.GetFromJsonAsync<ResponseGetUpdates>(url, serializerOptions);

                // todo: find out what to do with async
                getTask.Wait();

                TelegramApi.ResponseGetUpdates response = getTask.Result;
                //Console.WriteLine("Result:");
                //Console.WriteLine(resultStr);

                // Try to deserialize
                if (response.ok) {
                    // Update our nextUpdateId, we need it for requesting unconfirmed updates
                    foreach (TelegramApi.Update update in response.result) {
                        nextUpdateId = update.update_id + 1;
                    }
                    return response.result;
                } else {
                    return null;
                }
            }
            catch (Exception e) {
                Console.WriteLine($"Exception: {e}");
                return null;
            }
        }

        // Extracts command from message string
        // "/start" - returns "/start"
        // "/start@BotName" - returns "/start"
        // "Whatever" - returns null
        public string GetCommand(string msg) {
            if (msg.StartsWith('/')) {
                int iEndCmd = -1;
                for (int i = 0; i < msg.Length; i++) {
                    if (msg[i] == ' ' || msg[i] == '@') {
                        iEndCmd = i - 1;
                        break;
                    }
                }
                if (iEndCmd != -1) {                            // There is something else in the message
                    if (this.MentionsMe(msg))                   // Check if this bot is mentioned
                        return msg.Substring(0, iEndCmd + 1);
                    else
                        return null;                            // This bot is not mentioned
                } else {
                    return new string(msg);
                }
            } else {
                return null;
            }
        }
    }


    // ==== Telegram API types ====
    public class User {
        public User() { }

        public long id;
        public bool is_bot;
        public string first_name;
        public string last_name;
        public string username;
    }

    public class Chat {
        public Chat() { }
        public long id;
        public string type;
        public string title;

    }

    public class Message {
        public Message() { }
        public int message_id;
        public User from;
        public Chat sender_chat;
        //public ulong date;
        public Chat chat;
        public string text;
    }

    public class Update {
        public Update() { }
        public int update_id;
        public Message message;
        public Message channel_post;
    }

    public class ResponseGetUpdates {
        public ResponseGetUpdates() { }
        public bool ok;
        public Update[] result;
        public string description;
        public int testint;
    }

    public class KeyboardButton {
        public KeyboardButton() { }
        public string text;
        public bool request_contact = false;
        public bool request_location = false;
    }

    public abstract class KeyboardMarkup { }

    public class ReplyKeyboardMarkup : KeyboardMarkup {
        public ReplyKeyboardMarkup() { }
        public KeyboardButton[][] keyboard;
        public bool resize_keyboard = false;
        public bool one_time_keyboard = false;
        public bool selective = false;
    }

    public class ReplyKeyboardRemove {
        public ReplyKeyboardRemove() { }
        public bool remove_keyboard = true;
    }

    // ==== Telegram API method parameters ====


    public class MethodSendMessage {
        public MethodSendMessage() { }
        public long chat_id;
        public string text;
        public bool disable_web_page_preview = false;
        public bool disable_notification = false;
        public /* KeyboardMarkup */ object reply_markup = null;
    }


}
