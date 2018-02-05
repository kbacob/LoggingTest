using System;
using System.Threading;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace ConsoleApp1
{
    class Program
    {
        static string strTestLogFile = @"c:\temp\tst.log";
        static Logging logging = new Logging(strTestLogFile)
        {
            level = Logging.Level.Verbose,
            receiver = Logging.Receiver.File,
            timeFormat = Logging.TimeFormat.ShortLocalized,
            intBufferLength = 200,
            intFlushSleepInterval = 1000,
            boolColorisedConsoleOutput = true,
            boolSortByTime = false
        };
        static int cnt = 0;
        static int maxcnt = 10000;

        [MTAThread]
        static void Main(string[] args)
        {
            int intThreads = 100;
            Thread[] tMassLogSpammer = new Thread[intThreads];

            logging.Verbose("Begin", boolRealTimeFlush: true);

            for (var intTmp = 0; intTmp < tMassLogSpammer.Length; intTmp++)
            {
                tMassLogSpammer[intTmp] = new Thread(TestTask)
                {
                    Name = "Поток " + intTmp.ToString("D3")
                };
                tMassLogSpammer[intTmp].Start();
            }
            while (cnt < maxcnt) Thread.Sleep(250);
            for (var intTmp = 0; intTmp < tMassLogSpammer.Length; intTmp++)
            {
                tMassLogSpammer[intTmp].Interrupt();
            }

            logging.Verbose("End");

            logging.Dispose();

            FindDups(strTestLogFile);
            return;
        }

        static void TestTask()
        {
            while (true || cnt < maxcnt)
            {
                try
                {
                    logging.Verbose("[" + cnt++.ToString("D8") + "] " + Thread.CurrentThread.Name + ": " + DateTime.Now.ToString("r"));
                    Thread.Sleep(1);
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }
            }
        }
        static void FindDups(string strFilename)
        {
            var lStrings = new System.Collections.Generic.List<string>();

            using (var objStream = new System.IO.StreamReader(strFilename, System.Text.Encoding.UTF8))
            {
                if (objStream != null)
                {
                    Console.WriteLine("Begin check dupe strings...\n");

                    while (!objStream.EndOfStream)
                    {
                        var strNewString = objStream.ReadLine();
                        var intFoundDupeIndex = lStrings.IndexOf(strNewString);
                        var longProgress = objStream.BaseStream.Position / (objStream.BaseStream.Length / 100);

                        Console.CursorLeft = 0;
                        Console.Write(longProgress.ToString("D3") + "%");

                        if (intFoundDupeIndex != -1)
                        {
                            Console.CursorLeft = 0;
                            Console.WriteLine("Found dupe: " + intFoundDupeIndex.ToString() + " = " + (lStrings.Count - 1));
                        }
                        else lStrings.Add(strNewString);
                    }
                    objStream.Close();
                }
            }

            lStrings.Clear();
            Console.WriteLine("\n\nEnd");
            Console.ReadKey();
        }
    }

    /// <summary>
    /// Система записи и/или отображения отладочных сообщений.  
    /// <para>Позволяет просто указать, куда писать и дальше тупо вызывать Logging.WriteLine("BlaBlaBla"), что на выходе даст файл со строками вида "1980-01-01 12:12:14 UTC [I] BlaBlaBla"</para>
    /// <para>При необходимости кастомизируются форматы префикса времени и типа сообщения</para>
    /// <para>Сейчас пишет на терминал запуска, в файл или в Debug-косоль dotnet/VisualStudio.</para>
    /// </summary>
    public class Logging : List<Tuple<DateTime, Logging.Level, string>>, IDisposable
    {
        /// <summary>
        /// Куда выводить сообщения.
        /// </summary>
        [Flags]
        public enum Receiver : uint
        {
            /// <summary>
            /// Выводить сообщения на консоль
            /// </summary>
            Console = 1,
            /// <summary>
            /// Выводить сообщения в консоль отладки
            /// </summary>
            Debug = 2,
            /// <summary>
            /// Записывать сообщения в файл
            /// </summary>
            File = 4,
            /// <summary>
            /// [TODO] Записывать сообщения в лог системы, пока что пишутся в файл 
            /// </summary>
            Syslog = 8
        }
        /// <summary>
        /// Уровень (тип) сообщения.
        /// </summary>
        public enum Level
        {
            Verbose = 1,
            Debug,
            Info,
            Warning,
            Error,
            Critical,
            Fatal,
            None = int.MaxValue
        }
        /// <summary>
        /// Формат префикса строки сообщения с указанием времени события 
        /// </summary>
        public enum TimeFormat
        {
            /// <summary>
            /// Не выводить время
            /// </summary>
            None,
            /// <summary>
            /// Короткое локальное время
            /// </summary>
            ShortLocalized,
            /// <summary>
            /// Полное локальное время
            /// </summary>
            FullLocalized,
            /// <summary>
            /// Короткое универсальное время
            /// </summary>
            ShortUTC,
            /// <summary>
            /// Длинное универсальное время
            /// </summary>
            FullUTC
        }

        private Thread threadFlusher = null;
        private int intStoredPointer = 0;
        private bool boolStop = false;
        private bool boolSafeForEnd = false;
        private bool boolDisposed = true;
        private bool boolBusy = false;
        private bool boolSaving = false;
        private int intMaxBufferCount = 0;
        private uint uintTotalStrings = 0;

        private string strLogfile;

        private static Dictionary<Level, String> LMessageTypePrefix = null;
        private static Dictionary<TimeFormat, String> LTimeFormat = null;

        /// <summary>
        /// Время простоя цикла диспетчера буфера сообщений, мсек.
        /// </summary>
        public int intFlushSleepInterval = 1000;
        /// <summary>
        ///  Чиисло строк, хранимых в буффере сообщений.
        /// </summary>
        public int intBufferLength = 200;
        /// <summary>
        /// Выбранные устройства для отображения сообщений. Допускаются битовые маски для вывода сообщений сразу в несколько мест.
        /// </summary>
        public Receiver receiver = Receiver.Debug;
        /// <summary>
        /// Выбранный уровень фильтрации сообщений. Все, что "меньше" этого уровня, будет проигнорировано.
        /// </summary>
        public Level level = Level.Debug;
        /// <summary>
        /// Использовать-ли цветовую раскраску при выводе сообщений на терминале запуска.
        /// </summary>
        public bool boolColorisedConsoleOutput = true;
        /// <summary>
        /// Сортировать ли сообщения перед их отображением/записью по времени их создания. Нужно, чтобы избежать каши в мультипоточных приложениях. 
        /// <para>Незначительно замедляет работу, но это когда сообщений уже за сотню тысяч</para>
        /// </summary>
        public bool boolSortByTime = false;
        /// <summary>
        /// Выбранный формат отображения временного префикса.
        /// </summary>
        public TimeFormat timeFormat = TimeFormat.ShortLocalized;

        /// <summary>
        /// Создание экземпляра логгера. Перед использованием нужно будет отконфигурировать под свои нужды - поскольку, по умолчанию, сообщения будут писаться только в Debug-консоль и фильтроваться уровнем не ниже Debug. Далее пример конфигурирования:
        /// <para>Logging logging = new Logging(@"c:\temp\tst.log"){receiver = Logging.Receiver.File | Logging.Receiver.Debug, level = Logging.Level.Info, timeFormat = Logging.TimeFormat.ShortLocalized, boolSortByTime = true};</para>
        /// </summary>
        /// <param name="strLogfile">Путь к файлу для записи сообщений.</param>
        public Logging(string strLogfile = null)
        {
            Clear();
            boolDisposed = false;

            LMessageTypePrefix = new Dictionary<Level, string>
            {
                { Level.Verbose , "[V]" },
                { Level.Debug   , "[D]" },
                { Level.Info    , "[I]" },
                { Level.Warning , "[W]" },
                { Level.Error   , "[E]" },
                { Level.Critical, "[C]" },
                { Level.Fatal   , "[!]" }
            };

            LTimeFormat = new Dictionary<TimeFormat, string>
            {
                { TimeFormat.None, "" },
                { TimeFormat.ShortLocalized, "yyyyMMddHHmmss" },
                { TimeFormat.FullLocalized , "yyyy/MM/dd HH:mm:ss:ffff K" },
                { TimeFormat.ShortUTC      , "yyyyMMddHHmmss" },
                { TimeFormat.FullUTC       , "yyyy/MM/dd HH:mm:ss:ffff UTC" }
            };

            this.strLogfile = strLogfile;
            threadFlusher = new Thread(AutoFlush);
            threadFlusher.Start();
        }
        ~Logging() => Dispose();
        /// <summary>
        /// Отображение/запись всего, что ещё не успели показать и освобождение занятых ресурсов.
        /// </summary>
        public void Dispose()
        {
            if (boolDisposed) return;

            boolStop = true;
            while (!boolSafeForEnd || boolBusy) Thread.Sleep(0);
            if (intStoredPointer != Count)
            {
                Flush();
                while (boolBusy || boolSaving) Thread.Sleep(0);
            }
            System.Diagnostics.Debug.WriteLine("Total strings: " + uintTotalStrings + ", max buffer count: " + intMaxBufferCount);
            boolDisposed = true;

            Clear();
            LTimeFormat.Clear();
            LMessageTypePrefix.Clear();
        }

        private string MessagePrefixString(Level level) => LMessageTypePrefix[level];
        private string TimePrefixString(TimeFormat timeFormat, DateTime dateTime)
        {
            switch (timeFormat)
            {
                case TimeFormat.None:
                    return "?";

                case TimeFormat.ShortLocalized:
                case TimeFormat.FullLocalized:
                    return dateTime.ToString(LTimeFormat[timeFormat]);

                case TimeFormat.ShortUTC:
                case TimeFormat.FullUTC:
                    return dateTime.ToUniversalTime().ToString(LTimeFormat[timeFormat]);
            }
            return null;
        }
        private void ColorisedOutput(string strMessage, ConsoleColor colorForeground, ConsoleColor colorBackground)
        {
            var colorStoredForeground = Console.ForegroundColor;
            var colorStoredBackground = Console.BackgroundColor;

            Console.ForegroundColor = colorForeground;
            Console.BackgroundColor = colorBackground;
            Console.Write(strMessage);

            Console.ForegroundColor = colorStoredForeground;
            Console.BackgroundColor = colorStoredBackground;
        }
        private void SendToConsole(int intMessageIndex)
        {
            if (!boolColorisedConsoleOutput)
            {
                Console.WriteLine(TimePrefixString(timeFormat, this[intMessageIndex].Item1) + " " + MessagePrefixString(this[intMessageIndex].Item2) + ": " + this[intMessageIndex].Item3);
            }
            else
            {
                var colorForeground = ConsoleColor.Gray;
                var colorBackground = ConsoleColor.Black;
                var colorTimeForeground = colorForeground;
                var colorTimeBackground = colorBackground;
                var colorPrefixForeground = colorForeground;
                var colorPrefixBackground = colorBackground;
                var colorMessageForeground = colorForeground;
                var colorMessageBackground = colorBackground;

                switch (this[intMessageIndex].Item2)
                {
                    case Level.Verbose:
                        break;

                    case Level.Debug:
                        colorPrefixForeground = ConsoleColor.Green;
                        colorMessageForeground = colorPrefixForeground;
                        break;

                    case Level.Info:
                        colorPrefixForeground = ConsoleColor.White;
                        colorMessageForeground = colorPrefixForeground;
                        break;

                    case Level.Warning:
                        colorPrefixForeground = ConsoleColor.Yellow;
                        colorMessageForeground = colorPrefixForeground;
                        break;

                    case Level.Error:
                        colorPrefixForeground = ConsoleColor.Red;
                        colorMessageForeground = colorPrefixForeground;
                        break;

                    case Level.Critical:
                        colorPrefixForeground = ConsoleColor.Red;
                        colorMessageForeground = ConsoleColor.White;
                        colorMessageBackground = ConsoleColor.Red;
                        break;

                    case Level.Fatal:
                        colorForeground = ConsoleColor.Yellow;
                        colorBackground = ConsoleColor.Red;
                        colorTimeForeground = colorForeground;
                        colorTimeBackground = colorBackground;
                        colorPrefixForeground = colorTimeForeground;
                        colorPrefixBackground = colorTimeBackground;
                        colorMessageForeground = colorTimeForeground;
                        colorMessageBackground = colorTimeBackground;
                        break;
                }

                ColorisedOutput(TimePrefixString(timeFormat, this[intMessageIndex].Item1), colorTimeForeground, colorTimeBackground);
                ColorisedOutput(" ", colorForeground, colorBackground);
                ColorisedOutput(MessagePrefixString(this[intMessageIndex].Item2), colorPrefixForeground, colorPrefixBackground);
                ColorisedOutput(": ", colorForeground, colorBackground);
                ColorisedOutput(this[intMessageIndex].Item3, colorMessageForeground, colorMessageBackground);
                Console.Write("\n");
            }
        }
        private void SendToDebug(int intMessageIndex)
        {
            System.Diagnostics.Debug.WriteLine(TimePrefixString(timeFormat, this[intMessageIndex].Item1) + " " + MessagePrefixString(this[intMessageIndex].Item2) + ": " + this[intMessageIndex].Item3);
        }
        private void SendToSyslog(int intMessageIndex)
        {
            throw new NotImplementedException("Receiver.Syslog");
        }
        private async void SendToFile(string strData)
        {
            if (String.IsNullOrEmpty(strLogfile)) throw new NullReferenceException(GetType().FullName + ".strLogfile");
            if (strData == null) throw new NullReferenceException("strData");

            using (var objStream = new StreamWriter(strLogfile, true, Encoding.UTF8) { AutoFlush = true })
            {
                if (objStream != null)
                {
                    boolSaving = true;
                    await objStream.WriteAsync(strData);
                    objStream.Close();
                    boolSaving = false;
                }
            }
        }

        private void Flush()
        {
            if (boolBusy || boolSaving || Count == 0 || intStoredPointer == Count) return;

            boolBusy = true;
            if (boolSortByTime && Count > 1) Sort((a, b) => DateTime.Compare(a.Item1, b.Item1));

            var sbOutput = new StringBuilder("");

            if (Count > intMaxBufferCount) intMaxBufferCount = Count;
            for (; intStoredPointer < Count; intStoredPointer++)
            {
                uintTotalStrings++;
                if (this[intStoredPointer] != null && this[intStoredPointer].Item3 != null && this[intStoredPointer].Item2 >= level)
                {
                    if (receiver.HasFlag(Receiver.Debug)) SendToDebug(intStoredPointer);
                    if (receiver.HasFlag(Receiver.Console)) SendToConsole(intStoredPointer);
                    if (receiver.HasFlag(Receiver.Syslog)) SendToSyslog(intStoredPointer);
                    if (receiver.HasFlag(Receiver.File)) sbOutput.AppendLine(TimePrefixString(timeFormat, this[intStoredPointer].Item1) + " " + MessagePrefixString(this[intStoredPointer].Item2) + ": " + this[intStoredPointer].Item3);
                }
            }

            if (sbOutput.Length > 0 && receiver.HasFlag(Receiver.File)) SendToFile(sbOutput.ToString());
            sbOutput.Clear();

            if (Count >= intBufferLength)
            {
                Clear();
                intStoredPointer = 0;
            }
            boolBusy = false;
        }
        private void AutoFlush()
        {
            if (boolDisposed) throw new ObjectDisposedException(GetType().FullName);

            boolSafeForEnd = false;
            while (!boolStop)
            {
                Flush();
                Thread.Sleep(intFlushSleepInterval);
            }
            boolSafeForEnd = true;
        }

        /// <summary>
        /// Переопределение префиксов типов сообщений, если стандартные [D], [E], [C] и т.п. не устраивают
        /// </summary>
        /// <param name="messageType">Тип сообщения</param>
        /// <param name="strPrefix">Префикс для указания типа сообщения</param>
        public void SetPrefix(Level messageType, string strPrefix)
        {
            if (boolDisposed) throw new ObjectDisposedException(GetType().FullName);
            if (strPrefix == null) throw new ArgumentNullException("strPrefix");

            if (LMessageTypePrefix.ContainsKey(messageType))
            {
                LMessageTypePrefix[messageType] = strPrefix;
            }
            else throw new ArgumentOutOfRangeException("messageType");
        }
        /// <summary>
        /// <para>Переопределение формата строк с указанием времени события, если стандартные не устраивают.</para>
        /// По поводу формата смотри описания DateTime.Now.ToString(fmt) или DateTime.UtcNow.ToString(fmt) в документации от Microsoft
        /// </summary>
        /// <param name="timeFormat">Тип формата префикса времени сообщения</param>
        /// <param name="strTimeFormat">Строка, описывающая формат префикса времени сообщения</param>
        public void SetTimeFormat(TimeFormat timeFormat, string strTimeFormat)
        {
            if (boolDisposed) throw new ObjectDisposedException(GetType().FullName);
            if (strTimeFormat == null) throw new ArgumentNullException("strTimeFormat");

            if (LTimeFormat.ContainsKey(timeFormat))
            {
                LTimeFormat[timeFormat] = strTimeFormat;
            }
            else throw new ArgumentOutOfRangeException("timeFormat");
        }

        /// <summary>
        /// Отправить сообщение
        /// </summary>
        /// <param name="strMessage">Текст</param>
        /// <param name="logLevel">Уровень/тип</param>
        /// <param name="boolRealTimeFlush">Требуется немедленное отображние/запись</param>
        public void WriteLine(string strMessage, Level logLevel = Level.Debug, bool boolRealTimeFlush = false)
        {
            if (boolDisposed) throw new ObjectDisposedException(GetType().FullName);
            if (strMessage == null) throw new ArgumentNullException("strMessage");

            Add(new Tuple<DateTime, Level, string>(DateTime.Now, logLevel, strMessage));
            if (boolRealTimeFlush) Flush();
        }
        /// <summary>
        /// Отправить сообщение уровня Verbose (Подробности)
        /// </summary>
        /// <param name="strText">Текст сообщения.</param>
        /// <param name="boolRealTimeFlush">Требуется ли немедленный показ/запись сообщения.</param>
        public void Verbose(string strText, bool boolRealTimeFlush = false) => WriteLine(strText, Level.Verbose, boolRealTimeFlush);
        /// <summary>
        /// Отправить сообщение уровня Debug (Отладочное)
        /// </summary>
        /// <param name="strText">Текст сообщения.</param>
        /// <param name="boolRealTimeFlush">Требуется ли немедленный показ/запись сообщения.</param>
        public void Debug(string strText, bool boolRealTimeFlush = false) => WriteLine(strText, Level.Debug, boolRealTimeFlush);
        /// <summary>
        /// Отправить сообщение уровня Info (Информационное)
        /// </summary>
        /// <param name="strText">Текст сообщения.</param>
        /// <param name="boolRealTimeFlush">Требуется ли немедленный показ/запись сообщения.</param>
        public void Info(string strText, bool boolRealTimeFlush = false) => WriteLine(strText, Level.Info, boolRealTimeFlush);
        /// <summary>
        /// Отправить сообщение уровня Warning (Внимание, важно)
        /// </summary>
        /// <param name="strText">Текст сообщения.</param>
        /// <param name="boolRealTimeFlush">Требуется ли немедленный показ/запись сообщения.</param>
        public void Warning(string strText, bool boolRealTimeFlush = false) => WriteLine(strText, Level.Warning, boolRealTimeFlush);
        /// <summary>
        /// Отправить сообщение уровня Error (Не смертельная ошибка)
        /// </summary>
        /// <param name="strText">Текст сообщения.</param>
        /// <param name="boolRealTimeFlush">Требуется ли немедленный показ/запись сообщения.</param>
        public void Error(string strText, bool boolRealTimeFlush = false) => WriteLine(strText, Level.Error, boolRealTimeFlush);
        /// <summary>
        /// Отправить сообщение уровня Critical (Критическая ошибка, после которой программа должна корректно завершить работу)
        /// </summary>
        /// <param name="strText">Текст сообщения.</param>
        /// <param name="boolRealTimeFlush">Требуется ли немедленный показ/запись сообщения.</param>
        public void Critical(string strText, bool boolRealTimeFlush = false) => WriteLine(strText, Level.Critical, boolRealTimeFlush);
        /// <summary>
        /// Отправить сообщение уровня Fatal (Критическая ошибка, после которой программа должна немедленно завершить работу)
        /// </summary>
        /// <param name="strText">Текст сообщения.</param>
        /// <param name="boolRealTimeFlush">Требуется ли немедленный показ/запись сообщения.</param>
        public void Fatal(string strText, bool boolRealTimeFlush = false) => WriteLine(strText, Level.Fatal, boolRealTimeFlush);
    }
}
