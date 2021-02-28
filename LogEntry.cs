using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows;
using System.Diagnostics;

namespace RazzLogging
{
    public class LogEntry
    {
        public static Color DefaultColor { get; set; } = Color.FromArgb(System.Drawing.SystemColors.WindowText.A, System.Drawing.SystemColors.WindowText.R, System.Drawing.SystemColors.WindowText.G, System.Drawing.SystemColors.WindowText.B);
        private string message;
        private LogEntryType logMessageType;
        private DateTime timeStamp;

        public string Message
        {
            get
            {
                return message;
            }
        }
        public LogEntryType Type
        {
            get
            {
                return logMessageType;
            }
        }
        public DateTime TimeStamp
        {
            get
            {
                return timeStamp;
            }
        }
        public bool ShowTimeStamp { get; set; }
        public Color MessageColor
        {
            get
            {
                if (Type == LogEntryType.Success)
                {
                    return Color.FromRgb(50, 175, 50);
                }
                else if (Type == LogEntryType.Error)
                {
                    return Color.FromRgb(175, 50, 50);
                }
                return DefaultColor;
            }
        }
        public LogEntry(string msg, LogEntryType messageType)
        {
            message = msg;
            logMessageType = messageType;
            ShowTimeStamp = true;
            timeStamp = DateTime.Now;
        }
        public LogEntry(string msg) : this(msg, LogEntryType.Normal)
        {

        }
        override public string ToString()
        {
            return Message;
        }
        public static explicit operator Paragraph(LogEntry logMessage)
        {
            string msg = logMessage.Message;
            if (logMessage.ShowTimeStamp)
            {
                msg = $"{logMessage.TimeStamp.ToString()}: {msg}";
            }
            Run run = new Run(msg);
            run.Foreground = new SolidColorBrush(logMessage.MessageColor);
            Paragraph paragraph = new Paragraph(run);
            paragraph.Margin = new Thickness(0);
            return paragraph;
        }

        public static explicit operator Block(LogEntry logMessage)
        {
            return (Paragraph)logMessage;
        }
    }
    public enum LogEntryType
    {
        Normal,
        Success,
        Error
    }
    public class LoggedMessageEventArgs : EventArgs
    {
        private readonly LogEntry _logEntry;
        public LoggedMessageEventArgs(string message, LogEntryType logtype)
        {
            _logEntry = new LogEntry(message,logtype);
        }
        public LoggedMessageEventArgs(string message) : this(message, LogEntryType.Normal) { }
        public LoggedMessageEventArgs(LogEntry entry)
        {
            _logEntry = entry;
        }

        public LogEntry LogEntry
        {
            get { return _logEntry; }
        }
    }
}
