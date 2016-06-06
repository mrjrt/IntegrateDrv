//---------------------------------------------------------------------
// <copyright file="CabUnpacker.cs" company="Microsoft">
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
	using System.Text;
	using System.Security.Permissions;
	using System.Collections.Generic;
	using System.Globalization;
	using System.Runtime.InteropServices;
	using System.Diagnostics.CodeAnalysis;

	internal class CabUnpacker : CabWorker
	{
		private IntPtr _fdiHandle = IntPtr.Zero;

		// These delegates need to be saved as member variables
		// so that they don't get GC'd.
		// ReSharper disable PrivateFieldCanBeConvertedToLocalVariable
		private readonly NativeMethods.FDI.PFNALLOC _fdiAllocMemHandler;
		private readonly NativeMethods.FDI.PFNFREE _fdiFreeMemHandler;
		private readonly NativeMethods.FDI.PFNOPEN _fdiOpenStreamHandler;
		private readonly NativeMethods.FDI.PFNREAD _fdiReadStreamHandler;
		private readonly NativeMethods.FDI.PFNWRITE _fdiWriteStreamHandler;
		private readonly NativeMethods.FDI.PFNCLOSE _fdiCloseStreamHandler;
		private readonly NativeMethods.FDI.PFNSEEK _fdiSeekStreamHandler;

		private IUnpackStreamContext _context;

		private List<ArchiveFileInfo> _fileList;

		private int _folderID;

		private Predicate<string> _filter;

		[SuppressMessage("Microsoft.Security", "CA2106:SecureAsserts")]
		[SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
		[SecurityPermission(SecurityAction.Assert, UnmanagedCode = true)]
		public CabUnpacker(CabEngine cabEngine)
			: base(cabEngine)
		{
			_fdiAllocMemHandler = CabAllocMem;
			_fdiFreeMemHandler = CabFreeMem;
			_fdiOpenStreamHandler = CabOpenStream;
			_fdiReadStreamHandler = CabReadStream;
			_fdiWriteStreamHandler = CabWriteStream;
			_fdiCloseStreamHandler = CabCloseStream;
			_fdiSeekStreamHandler = CabSeekStream;

			_fdiHandle = NativeMethods.FDI.Create(
				_fdiAllocMemHandler,
				_fdiFreeMemHandler,
				_fdiOpenStreamHandler,
				_fdiReadStreamHandler,
				_fdiWriteStreamHandler,
				_fdiCloseStreamHandler,
				_fdiSeekStreamHandler,
				NativeMethods.FDI.CPU_80386,
				ErfHandle.AddrOfPinnedObject());
			if (Erf.Error)
			{
				var error = Erf.Oper;
				var errorCode = Erf.Type;
				ErfHandle.Free();
				throw new CabException(
					error,
					errorCode,
					CabException.GetErrorMessage(error, errorCode, true));
			}
		}

		[SuppressMessage("Microsoft.Security", "CA2106:SecureAsserts")]
		[SecurityPermission(SecurityAction.Assert, UnmanagedCode = true)]
		public bool IsArchive(Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException("stream");

			lock (this)
			{
				short id;
				int folderCount, fileCount;
				return IsCabinet(stream, out id, out folderCount, out fileCount);
			}
		}

		[SuppressMessage("Microsoft.Security", "CA2106:SecureAsserts")]
		[SecurityPermission(SecurityAction.Assert, UnmanagedCode = true)]
		public IList<ArchiveFileInfo> GetFileInfo(
			IUnpackStreamContext streamContext,
			Predicate<string> fileFilter)
		{
			if (streamContext == null)
				throw new ArgumentNullException("streamContext");

			lock (this)
			{
				_context = streamContext;
				_filter = fileFilter;
				NextCabinetName = string.Empty;
				_fileList = new List<ArchiveFileInfo>();
				var tmpSuppress = SuppressProgressEvents;
				SuppressProgressEvents = true;
				try
				{
					for (short cabNumber = 0;
						 NextCabinetName != null;
						 cabNumber++)
					{
						Erf.Clear();
						CabNumbers[NextCabinetName] = cabNumber;
						
						NativeMethods.FDI.Copy(
							_fdiHandle,
							NextCabinetName,
							string.Empty,
							0,
							CabListNotify,
							IntPtr.Zero,
							IntPtr.Zero);
						CheckError(true);
					}

					var tmpFileList = _fileList;
					_fileList = null;
					return tmpFileList.AsReadOnly();
				}
				finally
				{
					SuppressProgressEvents = tmpSuppress;

					if (CabStream != null)
					{
						_context.CloseArchiveReadStream(
							CurrentArchiveNumber,
							CurrentArchiveName,
							CabStream);
						CabStream = null;
					}

					_context = null;
				}
			}
		}

		[SuppressMessage("Microsoft.Security", "CA2106:SecureAsserts")]
		[SecurityPermission(SecurityAction.Assert, UnmanagedCode = true)]
		public void Unpack(
			IUnpackStreamContext streamContext,
			Predicate<string> fileFilter)
		{
			lock (this)
			{
				var files =
					GetFileInfo(streamContext, fileFilter);

				ResetProgressData();

				if (files != null)
				{
					TotalFiles = files.Count;

					for (var i = 0; i < files.Count; i++)
					{
						TotalFileBytes += files[i].Length;
						if (files[i].ArchiveNumber >= TotalArchives)
						{
							var totalArchives = files[i].ArchiveNumber + 1;
							TotalArchives = (short) totalArchives;
						}
					}
				}

				_context = streamContext;
				_fileList = null;
				NextCabinetName = string.Empty;
				_folderID = -1;
				CurrentFileNumber = -1;

				try
				{
					for (short cabNumber = 0;
						 NextCabinetName != null;
						 cabNumber++)
					{
						Erf.Clear();
						CabNumbers[NextCabinetName] = cabNumber;

						NativeMethods.FDI.Copy(
							_fdiHandle,
							NextCabinetName,
							string.Empty,
							0,
							CabExtractNotify,
							IntPtr.Zero,
							IntPtr.Zero);
						CheckError(true);
					}
				}
				finally
				{
					if (CabStream != null)
					{
						_context.CloseArchiveReadStream(
							CurrentArchiveNumber,
							CurrentArchiveName,
							CabStream);
						CabStream = null;
					}

					if (FileStream != null)
					{
						_context.CloseFileWriteStream(CurrentFileName, FileStream, FileAttributes.Normal, DateTime.Now);
						FileStream = null;
					}

					_context = null;
				}
			}
		}

		internal override int CabOpenStreamEx(string path, int openFlags, int shareMode, out int err, IntPtr pv)
		{
			if (CabNumbers.ContainsKey(path))
			{
				var stream = CabStream;
				if (stream == null)
				{
					var cabNumber = CabNumbers[path];

					stream = _context.OpenArchiveReadStream(cabNumber, path, CabEngine);
					if (stream == null)
						throw new FileNotFoundException(string.Format(CultureInfo.InvariantCulture, "Cabinet {0} not provided.", cabNumber));
					CurrentArchiveName = path;
					CurrentArchiveNumber = cabNumber;
					if (TotalArchives <= CurrentArchiveNumber)
					{
						var totalArchives = CurrentArchiveNumber + 1;
						TotalArchives = (short) totalArchives;
					}
					CurrentArchiveTotalBytes = stream.Length;
					CurrentArchiveBytesProcessed = 0;

					if (_folderID != -3) // -3 is a special folderId that requires re-opening the same cab

						OnProgress(ArchiveProgressType.StartArchive);
					CabStream = stream;
				}
				path = CabStreamName;
			}
			return base.CabOpenStreamEx(path, openFlags, shareMode, out err, pv);
		}

		internal override int CabReadStreamEx(int streamHandle, IntPtr memory, int cb, out int err, IntPtr pv)
		{
			var count = base.CabReadStreamEx(streamHandle, memory, cb, out err, pv);
			if (err == 0 && CabStream != null)
			{
				if (_fileList == null)
				{
					var stream = StreamHandles[streamHandle];
					if (DuplicateStream.OriginalStream(stream) ==
						DuplicateStream.OriginalStream(CabStream))
					{
						CurrentArchiveBytesProcessed += cb;
						if (CurrentArchiveBytesProcessed > CurrentArchiveTotalBytes)
							CurrentArchiveBytesProcessed = CurrentArchiveTotalBytes;
					}
				}
			}
			return count;
		}

		internal override int CabWriteStreamEx(int streamHandle, IntPtr memory, int cb, out int err, IntPtr pv)
		{
			var count = base.CabWriteStreamEx(streamHandle, memory, cb, out err, pv);
			if (count > 0 && err == 0)
			{
				CurrentFileBytesProcessed += cb;
				FileBytesProcessed += cb;
				OnProgress(ArchiveProgressType.PartialFile);
			}
			return count;
		}

		internal override int CabCloseStreamEx(int streamHandle, out int err, IntPtr pv)
		{
			var stream = DuplicateStream.OriginalStream(StreamHandles[streamHandle]);

			if (stream == DuplicateStream.OriginalStream(CabStream))
			{
				if (_folderID != -3) // -3 is a special folderId that requires re-opening the same cab

					OnProgress(ArchiveProgressType.FinishArchive);

				_context.CloseArchiveReadStream(CurrentArchiveNumber, CurrentArchiveName, stream);

				CurrentArchiveName = NextCabinetName;
				CurrentArchiveBytesProcessed = CurrentArchiveTotalBytes = 0;

				CabStream = null;
			}
			return base.CabCloseStreamEx(streamHandle, out err, pv);
		}

		/// <summary>
		/// Disposes of resources allocated by the cabinet engine.
		/// </summary>
		/// <param name="disposing">If true, the method has been called directly or indirectly by a user's code,
		/// so managed and unmanaged resources will be disposed. If false, the method has been called by the 
		/// runtime from inside the finalizer, and only unmanaged resources will be disposed.</param>
		[SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
		protected override void Dispose(bool disposing)
		{
			try
			{
				if (disposing)
				{
					if (_fdiHandle != IntPtr.Zero)
					{
						NativeMethods.FDI.Destroy(_fdiHandle);
						_fdiHandle = IntPtr.Zero;
					}
				}
			}
			finally
			{
				base.Dispose(disposing);
			}
		}

		private static string GetFileName(NativeMethods.FDI.NOTIFICATION notification)
		{
			var utf8Name = (notification.attribs & (ushort) FileAttributes.Normal) != 0;  // _A_NAME_IS_UTF

			// Non-utf8 names should be completely ASCII. But for compatibility with
			// legacy tools, interpret them using the current (Default) ANSI codepage.
			var nameEncoding = utf8Name ? Encoding.UTF8 : Encoding.Default;

			// Find how many bytes are in the string.
			// Unfortunately there is no faster way.
			var nameBytesCount = 0;
			while (Marshal.ReadByte(notification.psz1, nameBytesCount) != 0)
				nameBytesCount++;

			var nameBytes = new byte[nameBytesCount];
			Marshal.Copy(notification.psz1, nameBytes, 0, nameBytesCount);
			var name = nameEncoding.GetString(nameBytes);
			if (Path.IsPathRooted(name))
				name = name.Replace("" + Path.VolumeSeparatorChar, "");

			return name;
		}

		private bool IsCabinet(Stream cabStream, out short id, out int cabFolderCount, out int fileCount)
		{
			var streamHandle = StreamHandles.AllocHandle(cabStream);
			try
			{
				Erf.Clear();
				NativeMethods.FDI.CABINFO fdici;
				var isCabinet = 0 != NativeMethods.FDI.IsCabinet(_fdiHandle, streamHandle, out fdici);

				if (Erf.Error)
				{
					if (((NativeMethods.FDI.ERROR) Erf.Oper) == NativeMethods.FDI.ERROR.UNKNOWN_CABINET_VERSION)
						isCabinet = false;
					else
					{
						throw new CabException(
							Erf.Oper,
							Erf.Type,
							CabException.GetErrorMessage(Erf.Oper, Erf.Type, true));
					}
				}

				id = fdici.setID;
				cabFolderCount = fdici.cFolders;
				fileCount = fdici.cFiles;
				return isCabinet;
			}
			finally
			{
				StreamHandles.FreeHandle(streamHandle);
			}
		}

		private int CabListNotify(NativeMethods.FDI.NOTIFICATIONTYPE notificationType, NativeMethods.FDI.NOTIFICATION notification)
		{
			switch (notificationType)
			{
				case NativeMethods.FDI.NOTIFICATIONTYPE.CABINET_INFO:
				{
					var nextCab = Marshal.PtrToStringAnsi(notification.psz1);
					NextCabinetName = (nextCab.Length != 0 ? nextCab : null);
					return 0;  // Continue
				}
				case NativeMethods.FDI.NOTIFICATIONTYPE.PARTIAL_FILE:
				{
					// This notification can occur when examining the contents of a non-first cab file.
					return 0;  // Continue
				}
				case NativeMethods.FDI.NOTIFICATIONTYPE.COPY_FILE:
				{
					//bool execute = (notification.attribs & (ushort) FileAttributes.Device) != 0;  // _A_EXEC

					var name = GetFileName(notification);

					if (_filter == null || _filter(name))
					{
						if (_fileList != null)
						{
							var attributes = (FileAttributes) notification.attribs &
								(FileAttributes.Archive | FileAttributes.Hidden | FileAttributes.ReadOnly | FileAttributes.System);
							if (attributes == 0)
								attributes = FileAttributes.Normal;
							DateTime lastWriteTime;
							CompressionEngine.DOSDateAndTimeToDateTime(notification.date, notification.time, out lastWriteTime);
							long length = notification.cb;

							var fileInfo = new CabFileInfo(
								name,
								notification.iFolder,
								notification.iCabinet,
								attributes,
								lastWriteTime,
								length);
							_fileList.Add(fileInfo);
							CurrentFileNumber = _fileList.Count - 1;
							FileBytesProcessed += notification.cb;
						}
					}

					TotalFiles++;
					TotalFileBytes += notification.cb;
					return 0;  // Continue
				}
			}
			return 0;
		}

		private int CabExtractNotify(NativeMethods.FDI.NOTIFICATIONTYPE notificationType, NativeMethods.FDI.NOTIFICATION notification)
		{
			switch (notificationType)
			{
				case NativeMethods.FDI.NOTIFICATIONTYPE.CABINET_INFO:
				{
					if (NextCabinetName != null && NextCabinetName.StartsWith("?", StringComparison.Ordinal))
					{
						// We are just continuing the copy of a file that spanned cabinets.
						// The next cabinet name needs to be preserved.
						NextCabinetName = NextCabinetName.Substring(1);
					}
					else
					{
						var nextCab = Marshal.PtrToStringAnsi(notification.psz1);
						NextCabinetName = (nextCab.Length != 0 ? nextCab : null);
					}
					return 0;  // Continue
				}
				case NativeMethods.FDI.NOTIFICATIONTYPE.NEXT_CABINET:
				{
					var nextCab = Marshal.PtrToStringAnsi(notification.psz1);
					CabNumbers[nextCab] = notification.iCabinet;
					NextCabinetName = "?" + NextCabinetName;
					return 0;  // Continue
				}
				case NativeMethods.FDI.NOTIFICATIONTYPE.COPY_FILE:
				{
					return CabExtractCopyFile(notification);
				}
				case NativeMethods.FDI.NOTIFICATIONTYPE.CLOSE_FILE_INFO:
				{
					return CabExtractCloseFile(notification);
				}
			}
			return 0;
		}

		private int CabExtractCopyFile(NativeMethods.FDI.NOTIFICATION notification)
		{
			if (notification.iFolder != _folderID)
			{
				if (notification.iFolder != -3)  // -3 is a special folderId used when continuing a folder from a previous cab
				{
					if (_folderID != -1) // -1 means we just started the extraction sequence
						CurrentFolderNumber++;
				}
				_folderID = notification.iFolder;
			}

			//bool execute = (notification.attribs & (ushort) FileAttributes.Device) != 0;  // _A_EXEC

			var name = GetFileName(notification);

			if (_filter == null || _filter(name))
			{
				CurrentFileNumber++;
				CurrentFileName = name;

				CurrentFileBytesProcessed = 0;
				CurrentFileTotalBytes = notification.cb;
				OnProgress(ArchiveProgressType.StartFile);

				DateTime lastWriteTime;
				CompressionEngine.DOSDateAndTimeToDateTime(notification.date, notification.time, out lastWriteTime);

				var stream = _context.OpenFileWriteStream(name, notification.cb, lastWriteTime);
				if (stream != null)
				{
					FileStream = stream;
					var streamHandle = StreamHandles.AllocHandle(stream);
					return streamHandle;
				}

				FileBytesProcessed += notification.cb;
				OnProgress(ArchiveProgressType.FinishFile);
				CurrentFileName = null;
			}
			return 0;  // Continue
		}

		private int CabExtractCloseFile(NativeMethods.FDI.NOTIFICATION notification)
		{
			var stream = StreamHandles[notification.hf];
			StreamHandles.FreeHandle(notification.hf);

			//bool execute = (notification.attribs & (ushort) FileAttributes.Device) != 0;  // _A_EXEC

			var name = GetFileName(notification);

			var attributes = (FileAttributes) notification.attribs &
				(FileAttributes.Archive | FileAttributes.Hidden | FileAttributes.ReadOnly | FileAttributes.System);
			if (attributes == 0)
				attributes = FileAttributes.Normal;
			DateTime lastWriteTime;
			CompressionEngine.DOSDateAndTimeToDateTime(notification.date, notification.time, out lastWriteTime);

			stream.Flush();
			_context.CloseFileWriteStream(name, stream, attributes, lastWriteTime);
			FileStream = null;

			var remainder = CurrentFileTotalBytes - CurrentFileBytesProcessed;
			CurrentFileBytesProcessed += remainder;
			FileBytesProcessed += remainder;
			OnProgress(ArchiveProgressType.FinishFile);
			CurrentFileName = null;

			return 1;  // Continue
		}
	}
}