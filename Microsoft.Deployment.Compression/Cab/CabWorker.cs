//---------------------------------------------------------------------
// <copyright file="CabWorker.cs" company="Microsoft">
//	Copyright (c) Microsoft Corporation.  All rights reserved.
//	
//	The use and distribution terms for this software are covered by the
//	Common Public License 1.0 (http://opensource.org/licenses/cpl.php)
//	which can be found in the file CPL.TXT at the root of this distribution.
//	By using this software in any fashion, you are agreeing to be bound by
//	the terms of this license.
//	
//	You must not remove this notice, or any other, from this software.
// </copyright>
// <summary>
// Part of the Deployment Tools Foundation project.
// </summary>
//---------------------------------------------------------------------

namespace Microsoft.Deployment.Compression.Cab
{
	using System;
	using System.IO;
	using System.Collections.Generic;
	using System.Runtime.InteropServices;
	using System.Diagnostics.CodeAnalysis;

	internal abstract class CabWorker : IDisposable
	{
		internal const string CabStreamName = "%%CAB%%";

		private GCHandle _erfHandle;

		private byte[] buf;

		// Progress data
		protected string CurrentFileName;
		protected int CurrentFileNumber;
		protected int TotalFiles;
		protected long CurrentFileBytesProcessed;
		protected long CurrentFileTotalBytes;
		protected short CurrentFolderNumber;
		protected long CurrentFolderTotalBytes;
		protected string CurrentArchiveName;
		protected short CurrentArchiveNumber;
		protected short TotalArchives;
		protected long CurrentArchiveBytesProcessed;
		protected long CurrentArchiveTotalBytes;
		protected long FileBytesProcessed;
		protected long TotalFileBytes;

		[SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
		protected CabWorker(CabEngine cabEngine)
		{
			CabEngine = cabEngine;
			StreamHandles = new HandleManager<Stream>();
			Erf = new NativeMethods.ERF();
			_erfHandle = GCHandle.Alloc(Erf, GCHandleType.Pinned);
			CabNumbers = new Dictionary<string, short>(1);

			// 32K seems to be the size of the largest chunks processed by cabinet.dll.
			// But just in case, this buffer will auto-enlarge.
			buf = new byte[32768];
		}

		~CabWorker()
		{
			Dispose(false);
		}

		public CabEngine CabEngine { get; private set; }

		internal NativeMethods.ERF Erf { get; private set; }

		internal GCHandle ErfHandle
		{
			get { return _erfHandle; }
		}

		internal HandleManager<Stream> StreamHandles { get; private set; }

		internal bool SuppressProgressEvents { get; set; }

		internal IDictionary<string, short> CabNumbers { get; private set; }

		internal string NextCabinetName { get; set; }

		internal Stream CabStream { get; set; }

		internal Stream FileStream { get; set; }

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected void ResetProgressData()
		{
			CurrentFileName = null;
			CurrentFileNumber = 0;
			TotalFiles = 0;
			CurrentFileBytesProcessed = 0;
			CurrentFileTotalBytes = 0;
			CurrentFolderNumber = 0;
			CurrentFolderTotalBytes = 0;
			CurrentArchiveName = null;
			CurrentArchiveNumber = 0;
			TotalArchives = 0;
			CurrentArchiveBytesProcessed = 0;
			CurrentArchiveTotalBytes = 0;
			FileBytesProcessed = 0;
			TotalFileBytes = 0;
		}

		protected void OnProgress(ArchiveProgressType progressType)
		{
			if (!SuppressProgressEvents)
			{
				var e = new ArchiveProgressEventArgs(
					progressType,
					CurrentFileName,
					CurrentFileNumber >= 0 ? CurrentFileNumber : 0,
					TotalFiles,
					CurrentFileBytesProcessed,
					CurrentFileTotalBytes,
					CurrentArchiveName,
					CurrentArchiveNumber,
					TotalArchives,
					CurrentArchiveBytesProcessed,
					CurrentArchiveTotalBytes,
					FileBytesProcessed,
					TotalFileBytes);
				CabEngine.ReportProgress(e);
			}
		}

		internal IntPtr CabAllocMem(int byteCount)
		{
			var memPointer = Marshal.AllocHGlobal((IntPtr) byteCount);
			return memPointer;
		}

		internal void CabFreeMem(IntPtr memPointer)
		{
			Marshal.FreeHGlobal(memPointer);
		}

		internal int CabOpenStream(string path, int openFlags, int shareMode)
		{
			int err;
			return CabOpenStreamEx(path, openFlags, shareMode, out err, IntPtr.Zero);
		}

		internal virtual int CabOpenStreamEx(string path, int openFlags, int shareMode, out int err, IntPtr pv)
		{
			path = path.Trim();
			var stream = CabStream;
			CabStream = new DuplicateStream(stream);
			var streamHandle = StreamHandles.AllocHandle(stream);
			err = 0;
			return streamHandle;
		}

		internal int CabReadStream(int streamHandle, IntPtr memory, int cb)
		{
			int err;
			return CabReadStreamEx(streamHandle, memory, cb, out err, IntPtr.Zero);
		}

		internal virtual int CabReadStreamEx(int streamHandle, IntPtr memory, int cb, out int err, IntPtr pv)
		{
			var stream = StreamHandles[streamHandle];
			var count = (int) cb;
			if (count > buf.Length)
				buf = new byte[count];
			count = stream.Read(buf, 0, count);
			Marshal.Copy(buf, 0, memory, count);
			err = 0;
			return count;
		}

		internal int CabWriteStream(int streamHandle, IntPtr memory, int cb)
		{
			int err;
			return CabWriteStreamEx(streamHandle, memory, cb, out err, IntPtr.Zero);
		}

		internal virtual int CabWriteStreamEx(int streamHandle, IntPtr memory, int cb, out int err, IntPtr pv)
		{
			var stream = StreamHandles[streamHandle];
			var count = (int) cb;
			if (count > buf.Length)
				buf = new byte[count];
			Marshal.Copy(memory, buf, 0, count);
			stream.Write(buf, 0, count);
			err = 0;
			return cb;
		}

		internal int CabCloseStream(int streamHandle)
		{
			int err;
			return CabCloseStreamEx(streamHandle, out err, IntPtr.Zero);
		}

		internal virtual int CabCloseStreamEx(int streamHandle, out int err, IntPtr pv)
		{
			StreamHandles.FreeHandle(streamHandle);
			err = 0;
			return 0;
		}

		internal int CabSeekStream(int streamHandle, int offset, int seekOrigin)
		{
			int err;
			return CabSeekStreamEx(streamHandle, offset, seekOrigin, out err, IntPtr.Zero);
		}

		internal virtual int CabSeekStreamEx(int streamHandle, int offset, int seekOrigin, out int err, IntPtr pv)
		{
			var stream = StreamHandles[streamHandle];
			offset = (int) stream.Seek(offset, (SeekOrigin) seekOrigin);
			err = 0;
			return offset;
		}

		/// <summary>
		/// Disposes of resources allocated by the cabinet engine.
		/// </summary>
		/// <param name="disposing">If true, the method has been called directly or indirectly by a user's code,
		/// so managed and unmanaged resources will be disposed. If false, the method has been called by the 
		/// runtime from inside the finalizer, and only unmanaged resources will be disposed.</param>
		[SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (CabStream != null)
				{
					CabStream.Close();
					CabStream = null;
				}

				if (FileStream != null)
				{
					FileStream.Close();
					FileStream = null;
				}
			}

			if (_erfHandle.IsAllocated)
			{
				_erfHandle.Free();
			}
		}

		protected void CheckError(bool extracting)
		{
			if (Erf.Error)
			{
				throw new CabException(
					Erf.Oper,
					Erf.Type,
					CabException.GetErrorMessage(Erf.Oper, Erf.Type, extracting));
			}
		}
	}
}