using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GitDepsPacker
{
	class WriteStream : Stream
	{
		Stream Inner;
		long Pos;

		public WriteStream(Stream Inner)
		{
			this.Inner = Inner;
			this.Pos = 0;
		}

		protected override void Dispose(bool Disposing)
		{
			if (Inner != null)
			{
				Inner.Dispose();
				Inner = null;
			}
		}

		public override bool CanRead
		{
			get { return false; }
		}

		public override bool CanWrite
		{
			get { return true; }
		}

		public override bool CanSeek
		{
			get { return false; }
		}

		public override long Position
		{
			get
			{
				return Pos;
			}
			set
			{
				throw new NotImplementedException();
			}
		}

		public override long Length
		{
			get { throw new NotImplementedException(); }
		}

		public override void SetLength(long Value)
		{
			throw new NotImplementedException();
		}

		public override int Read(byte[] Buffer, int Offset, int Count)
		{
			throw new NotImplementedException();
		}

		public override void Write(byte[] Buffer, int Offset, int Count)
		{
			Inner.Write(Buffer, Offset, Count);
			Pos += Count;
		}

		public override long Seek(long Offset, SeekOrigin Origin)
		{
			throw new NotImplementedException();
		}

		public override void Flush()
		{
			Inner.Flush();
		}
	}
}
