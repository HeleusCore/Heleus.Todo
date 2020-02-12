using Heleus.Base;
using System;

namespace Heleus.Apps.Shared
{
	static class UIAppSettings
	{
		public static bool AppReady = false;

        public static void ReadChunks(ChunkReader reader)
        {
			reader.Read(nameof(AppReady), ref AppReady);
		}

        public static void WriteChunks(ChunkWriter writer)
        {
			writer.Write(nameof(AppReady), AppReady);
		}
    }
}
