using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace TelegramWeatherBot {
    static class Logger {

        static FileStream f;

        public static void Init() {
            f = new FileStream("events.log", FileMode.Append, FileAccess.Write, FileShare.Read);
        }

        public static void LogLine(string s, bool timestamp = true) {
            if (timestamp) {
                DateTime now = DateTime.Now;
                string slog = $"{now} {s}";
                Console.WriteLine(slog);
                f.Write(Encoding.ASCII.GetBytes(slog));
                f.Write(Encoding.ASCII.GetBytes("\n"));
            } else {
                Console.WriteLine(s);
                f.Write(Encoding.ASCII.GetBytes(s));
                f.Write(Encoding.ASCII.GetBytes("\n"));
            }
            f.Flush();
        }

        public static void Log(string s, bool timestamp = true) {
            if (timestamp) {
                DateTime now = DateTime.Now;
                string slog = $"{now} {s}";
                Console.Write(slog);
                f.Write(Encoding.ASCII.GetBytes(slog));
                f.Write(Encoding.ASCII.GetBytes("\n"));
            } else {
                Console.Write(s);
                f.Write(Encoding.ASCII.GetBytes(s));
            }
            f.Flush();
        }
    }
}
