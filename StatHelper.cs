using System;
using System.IO;
using System.Reflection;

namespace GitDepsPacker
{
	static class StatFileHelper
	{
		const uint ExecutableBits = (1 << 0) | (1 << 3) | (1 << 6);

		private static MethodInfo StatMethod;
		private static FieldInfo StatModeField;

		static StatFileHelper()
		{
			StatMethod = null;

			// Try to load the Mono Posix assembly. If it doesn't exist, we're on Windows.
			Assembly MonoPosix;
			try
			{
				MonoPosix = Assembly.Load("Mono.Posix, Version=4.0.0.0, Culture=neutral, PublicKeyToken=0738eb9f132ed756");
			}
			catch (FileNotFoundException)
			{
				return;
			}
			Type SyscallType = MonoPosix.GetType("Mono.Unix.Native.Syscall");
			if(SyscallType == null)
			{
				throw new InvalidOperationException("Couldn't find Syscall type");
			}
			StatMethod = SyscallType.GetMethod ("stat");
			if (StatMethod == null)
			{
				throw new InvalidOperationException("Couldn't find Mono.Unix.Native.Syscall.stat method");
			}
			Type StatType = MonoPosix.GetType("Mono.Unix.Native.Stat");
			if(StatType == null)
			{
				throw new InvalidOperationException("Couldn't find Mono.Unix.Native.Stat type");
			}
			StatModeField = StatType.GetField("st_mode");
			if(StatModeField == null)
			{
				throw new InvalidOperationException("Couldn't find Mono.Unix.Native.Stat.st_mode field");
			}
		}

		public static bool IsExecutalbe(string FileName)
		{
			if (StatMethod == null) {
				return false;
			}

			object[] StatArgs = new object[] { FileName, null };
			int StatResult = (int)StatMethod.Invoke(null, StatArgs);
			if (StatResult != 0)
			{
				throw new InvalidOperationException(String.Format("Stat() call for {0} failed with error {1}", FileName, StatResult));
			}
			// Get the current permissions
			uint CurrentPermissions = (uint)StatModeField.GetValue(StatArgs[1]);
			return (CurrentPermissions & ExecutableBits) != 0;
		}
	}
}
