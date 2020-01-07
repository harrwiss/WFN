﻿using System;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using log4net;

#if DEBUG
using System.Runtime.CompilerServices;
#endif
using Wokhan.WindowsFirewallNotifier.Common.Properties;

namespace Wokhan.WindowsFirewallNotifier.Common.Helpers
{
    public static class LogHelper
    {
        private readonly static ILog LOGGER = LogManager.GetLogger(typeof(LogHelper));

        private static readonly bool IsAdmin = UacHelper.CheckProcessElevated();
        public static readonly string CurrentLogsPath;

        enum LogLevel
        {
            DEBUG, WARNING, INFO, ERROR
        }

        static LogHelper()
        {
            var assembly = Assembly.GetCallingAssembly().GetName();
            string appVersion = assembly.Version.ToString();
            string assemblyName = assembly.Name;
            CurrentLogsPath = AppDomain.CurrentDomain.BaseDirectory;

            if (Settings.Default?.FirstRun ?? true)
            {
                WriteLog(LogLevel.INFO, String.Format("OS: {0} ({1} bit) / .Net CLR: {2} / Path: {3} / Version: {4} ({5} bit)", Environment.OSVersion, Environment.Is64BitOperatingSystem ? 64 : 32, Environment.Version, AppDomain.CurrentDomain.BaseDirectory, appVersion, Environment.Is64BitProcess ? 64 : 32));
                WriteLog(LogLevel.INFO, $"Process elevated: {IsAdmin}");
            }
        }

#if DEBUG

        public static bool isDebugEnabled()
        {
            return true;
        }
        public static void Debug(string msg,
            [CallerMemberName] string memberName = null,
            [CallerFilePath] string filePath = null,
            [CallerLineNumber] int lineNumber = -1)
#else
        public static bool isDebugEnabled()
        {
            return false;
        }
        public static void Debug(string msg)
#endif
        {
            if (Settings.Default?.EnableVerboseLogging ?? false)
            {
#if DEBUG
                WriteLog(LogLevel.DEBUG, msg, memberName, filePath, lineNumber);
#else
                WriteLog(LogLevel.DEBUG, msg);
#endif
            }
        }

#if DEBUG
        public static void Info(string msg,
            [CallerMemberName] string memberName = null,
            [CallerFilePath] string filePath = null,
            [CallerLineNumber] int lineNumber = -1)
#else
        public static void Info(string msg)
#endif
        {
            if (Settings.Default?.EnableVerboseLogging ?? false)
            {
#if DEBUG
                WriteLog(LogLevel.INFO, msg, memberName, filePath, lineNumber);
#else
                WriteLog(LogLevel.INFO, msg);
#endif
            }
        }

#if DEBUG
        public static void Warning(string msg,
            [CallerMemberName] string memberName = null,
            [CallerFilePath] string filePath = null,
            [CallerLineNumber] int lineNumber = -1)
#else
        public static void Warning(string msg)
#endif
        {
#if DEBUG
            WriteLog(LogLevel.WARNING, msg, memberName, filePath, lineNumber);
#else
            WriteLog(LogLevel.WARNING, msg);
#endif
        }

#if DEBUG
        public static void Error(string msg, Exception e,
            [CallerMemberName] string memberName = null,
            [CallerFilePath] string filePath = null,
            [CallerLineNumber] int lineNumber = -1)
#else
        public static void Error(string msg, Exception e)
#endif
        {
#if DEBUG
            WriteLog(LogLevel.ERROR, msg + Environment.NewLine + (e != null ? e.GetType().ToString() + ": " + e.Message + Environment.NewLine + e.StackTrace : ""), memberName, filePath, lineNumber);
#else
            WriteLog(LogLevel.ERROR, msg + Environment.NewLine + (e != null ? e.GetType().ToString() + ": " + e.Message + Environment.NewLine + e.StackTrace : ""));
#endif
        }

        private static void WriteLog(LogLevel type, string msg, string memberName, string filePath, int lineNumber)
        {
            if (LogLevel.DEBUG.Equals(type))
            {
                LOGGER.Debug($"{msg} [{memberName}() in {Path.GetFileName(filePath)}, line {lineNumber}]");
            }
            else if(LogLevel.WARNING.Equals(type))
            {
                LOGGER.Warn($"{msg} [{memberName}() in {Path.GetFileName(filePath)}, line {lineNumber}]");
            }
            else if (LogLevel.ERROR.Equals(type))
            {
                LOGGER.Error($"{msg} [{memberName}()\n in {Path.GetFileName(filePath)}, line {lineNumber}]");
            } 
            else 
            {
                LOGGER.Info(msg);   
            }
        }

        private static void WriteLog(LogLevel type, string msg)
        {
            if (LogLevel.DEBUG.Equals(type))
            {
                LOGGER.Debug($"{msg}");
            }
            else if (LogLevel.WARNING.Equals(type))
            {
                LOGGER.Warn($"{msg}");
            }
            else if (LogLevel.ERROR.Equals(type))
            {
                LOGGER.Error($"{msg}");
            }
            else
            {
                LOGGER.Info(msg);
            }
        }

    }
}