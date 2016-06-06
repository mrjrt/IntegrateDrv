//---------------------------------------------------------------------
// <copyright file="CabPacker.cs" company="Microsoft">
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

	internal class CabPacker : CabWorker
	{
		private const string TempStreamName = "%%TEMP%%";

		private IntPtr _fciHandle = IntPtr.Zero;

		// These delegates need to be saved as member variables
		// so that they don't get GC'd.
		private readonly NativeMethods.FCI.PFNALLOC _fciAllocMemHandler;
		private readonly NativeMethods.FCI.PFNFREE _fciFreeMemHandler;
		private readonly NativeMethods.FCI.PFNOPEN _fciOpenStreamHandler;
		private readonly NativeMethods.FCI.PFNREAD _fciReadStreamHandler;
		private readonly NativeMethods.FCI.PFNWRITE _fciWriteStreamHandler;
		private readonly NativeMethods.FCI.PFNCLOSE _fciCloseStreamHandler;
		private readonly NativeMethods.FCI.PFNSEEK _fciSeekStreamHandler;
		private readonly NativeMethods.FCI.PFNFILEPLACED _fciFilePlacedHandler;
		private readonly NativeMethods.FCI.PFNDELETE _fciDeleteFileHandler;
		private readonly NativeMethods.FCI.PFNGETTEMPFILE _fciGetTempFileHandler;

		private readonly NativeMethods.FCI.PFNGETNEXTCABINET _fciGetNextCabinet;
		private readonly NativeMethods.FCI.PFNSTATUS _fciCreateStatus;
		private readonly NativeMethods.FCI.PFNGETOPENINFO _fciGetOpenInfo;

		private IPackStreamContext _context;

		private FileAttributes _fileAttributes;
		private DateTime _fileLastWriteTime;

		private int _maxCabBytes;

		private long _totalFolderBytesProcessedInCurrentCab;

		private bool _dontUseTempFiles;
		private readonly IList<Stream> _tempStreams;

		public CabPacker(CabEngine cabEngine)
			: base(cabEngine)
		{
			_fciAllocMemHandler	= CabAllocMem;
			_fciFreeMemHandler	 = CabFreeMem;
			_fciOpenStreamHandler  = CabOpenStreamEx;
			_fciReadStreamHandler  = CabReadStreamEx;
			_fciWriteStreamHandler = CabWriteStreamEx;
			_fciCloseStreamHandler = CabCloseStreamEx;
			_fciSeekStreamHandler  = CabSeekStreamEx;
			_fciFilePlacedHandler  = CabFilePlaced;
			_fciDeleteFileHandler  = CabDeleteFile;
			_fciGetTempFileHandler = CabGetTempFile;
			_fciGetNextCabinet	 = CabGetNextCabinet;
			_fciCreateStatus	   = CabCreateStatus;
			_fciGetOpenInfo		= CabGetOpenInfo;
			_tempStreams = new List<Stream>();
			CompressionLevel = CompressionLevel.Normal;
		}

		public bool UseTempFiles
		{
			get { return !_dontUseTempFiles; }
			set { _dontUseTempFiles = !value; }
		}

		public CompressionLevel CompressionLevel { get; set; }

		[SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
		private void CreateFci(long maxArchiveSize)
		{
			var ccab = new NativeMethods.FCI.CCAB();
			if (maxArchiveSize > 0 && maxArchiveSize < ccab.cb)
				ccab.cb = Math.Max(
					NativeMethods.FCI.MIN_DISK, (int) maxArchiveSize);

			var maxFolderSizeOption = _context.GetOption(
				"maxFolderSize", null);
			if (maxFolderSizeOption != null)
			{
				var maxFolderSize = Convert.ToInt64(
					maxFolderSizeOption, CultureInfo.InvariantCulture);
				if (maxFolderSize > 0 && maxFolderSize < ccab.cbFolderThresh)
					ccab.cbFolderThresh = (int) maxFolderSize;
			}

			_maxCabBytes = ccab.cb;
			ccab.szCab = _context.GetArchiveName(0);
			if (ccab.szCab == null)
				throw new FileNotFoundException("Cabinet name not provided by stream context.");

			ccab.setID = (short) new Random().Next(
				Int16.MinValue, Int16.MaxValue + 1);
			CabNumbers[ccab.szCab] = 0;
			CurrentArchiveName = ccab.szCab;
			TotalArchives = 1;
			CabStream = null;

			Erf.Clear();
			_fciHandle = NativeMethods.FCI.Create(
				ErfHandle.AddrOfPinnedObject(),
				_fciFilePlacedHandler,
				_fciAllocMemHandler,
				_fciFreeMemHandler,
				_fciOpenStreamHandler,
				_fciReadStreamHandler,
				_fciWriteStreamHandler,
				_fciCloseStreamHandler,
				_fciSeekStreamHandler,
				_fciDeleteFileHandler,
				_fciGetTempFileHandler,
				ccab,
				IntPtr.Zero);
			CheckError(false);
		}

		[SuppressMessage("Microsoft.Security", "CA2106:SecureAsserts")]
		[SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
		[SecurityPermission(SecurityAction.Assert, UnmanagedCode = true)]
		public void Pack(
			IPackStreamContext streamContext,
			IEnumerable<string> files,
			long maxArchiveSize)
		{
			if (streamContext == null)
				throw new ArgumentNullException("streamContext");

			if (files == null)
				throw new ArgumentNullException("files");

			lock (this)
			{
				try
				{
					_context = streamContext;

					ResetProgressData();

					CreateFci(maxArchiveSize);

					foreach (var file in files)
					{
						FileAttributes attributes;
						DateTime lastWriteTime;
						var fileStream = _context.OpenFileReadStream(
							file,
							out attributes,
							out lastWriteTime);
						if (fileStream != null)
						{
							TotalFileBytes += fileStream.Length;
							TotalFiles++;
							_context.CloseFileReadStream(file, fileStream);
						}
					}

					long uncompressedBytesInFolder = 0;
					CurrentFileNumber = -1;

					foreach (var file in files)
					{
						FileAttributes attributes;
						DateTime lastWriteTime;
						var fileStream = _context.OpenFileReadStream(
							file, out attributes, out lastWriteTime);
						if (fileStream == null)
							continue;

						if (fileStream.Length >= NativeMethods.FCI.MAX_FOLDER)
						{
							throw new NotSupportedException(string.Format(
								CultureInfo.InvariantCulture,
								"File {0} exceeds maximum file size " +
								"for cabinet format.",
								file));
						}

						if (uncompressedBytesInFolder > 0)
						{
							// Automatically create a new folder if this file
							// won't fit in the current folder.
							var nextFolder = uncompressedBytesInFolder
								+ fileStream.Length >= NativeMethods.FCI.MAX_FOLDER;

							// Otherwise ask the client if it wants to
							// move to the next folder.
							if (!nextFolder)
							{
								var nextFolderOption = streamContext.GetOption(
									"nextFolder",
									new object[] { file, CurrentFolderNumber });
								nextFolder = Convert.ToBoolean(
									nextFolderOption, CultureInfo.InvariantCulture);
							}

							if (nextFolder)
							{
								FlushFolder();
								uncompressedBytesInFolder = 0;
							}
						}

						if (CurrentFolderTotalBytes > 0)
						{
							CurrentFolderTotalBytes = 0;
							CurrentFolderNumber++;
							uncompressedBytesInFolder = 0;
						}

						CurrentFileName = file;
						CurrentFileNumber++;

						CurrentFileTotalBytes = fileStream.Length;
						CurrentFileBytesProcessed = 0;
						OnProgress(ArchiveProgressType.StartFile);

						uncompressedBytesInFolder += fileStream.Length;

						AddFile(
							file,
							fileStream,
							attributes,
							lastWriteTime,
							false,
							CompressionLevel);
					}

					FlushFolder();
					FlushCabinet();
				}
				finally
				{
					if (CabStream != null)
					{
						_context.CloseArchiveWriteStream(
							CurrentArchiveNumber,
							CurrentArchiveName,
							CabStream);
						CabStream = null;
					}

					if (FileStream != null)
					{
						_context.CloseFileReadStream(
							CurrentFileName, FileStream);
						FileStream = null;
					}
					_context = null;

					if (_fciHandle != IntPtr.Zero)
					{
						NativeMethods.FCI.Destroy(_fciHandle);
						_fciHandle = IntPtr.Zero;
					}
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

					CurrentFolderTotalBytes = 0;

					stream = _context.OpenArchiveWriteStream(cabNumber, path, true, CabEngine);
					if (stream == null)
					{
						throw new FileNotFoundException(
							string.Format(CultureInfo.InvariantCulture, "Cabinet {0} not provided.", cabNumber));
					}
					CurrentArchiveName = path;

					CurrentArchiveTotalBytes = Math.Min(
						_totalFolderBytesProcessedInCurrentCab, _maxCabBytes);
					CurrentArchiveBytesProcessed = 0;

					OnProgress(ArchiveProgressType.StartArchive);
					CabStream = stream;
				}
				path = CabStreamName;
			}
			else if (path == TempStreamName)
			{
				// Opening memory stream for a temp file.
				Stream stream = new MemoryStream();
				_tempStreams.Add(stream);
				var streamHandle = StreamHandles.AllocHandle(stream);
				err = 0;
				return streamHandle;
			}
			else if (path != CabStreamName)
			{
				// Opening a file on disk for a temp file.
				path = Path.Combine(Path.GetTempPath(), path);
				Stream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
				_tempStreams.Add(stream);
				stream = new DuplicateStream(stream);
				var streamHandle = StreamHandles.AllocHandle(stream);
				err = 0;
				return streamHandle;
			}
			return base.CabOpenStreamEx(path, openFlags, shareMode, out err, pv);
		}

		internal override int CabWriteStreamEx(int streamHandle, IntPtr memory, int cb, out int err, IntPtr pv)
		{
			var count = base.CabWriteStreamEx(streamHandle, memory, cb, out err, pv);
			if (count > 0 && err == 0)
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
			return count;
		}

		internal override int CabCloseStreamEx(int streamHandle, out int err, IntPtr pv)
		{
			var stream = DuplicateStream.OriginalStream(StreamHandles[streamHandle]);

			if (stream == DuplicateStream.OriginalStream(FileStream))
			{
				_context.CloseFileReadStream(CurrentFileName, stream);
				FileStream = null;
				var remainder = CurrentFileTotalBytes - CurrentFileBytesProcessed;
				CurrentFileBytesProcessed += remainder;
				FileBytesProcessed += remainder;
				OnProgress(ArchiveProgressType.FinishFile);

				CurrentFileTotalBytes = 0;
				CurrentFileBytesProcessed = 0;
				CurrentFileName = null;
			}
			else if (stream == DuplicateStream.OriginalStream(CabStream))
			{
				if (stream.CanWrite)
					stream.Flush();

				CurrentArchiveBytesProcessed = CurrentArchiveTotalBytes;
				OnProgress(ArchiveProgressType.FinishArchive);
				CurrentArchiveNumber++;
				TotalArchives++;

				_context.CloseArchiveWriteStream(
					CurrentArchiveNumber,
					CurrentArchiveName,
					stream);

				CurrentArchiveName = NextCabinetName;
				CurrentArchiveBytesProcessed = CurrentArchiveTotalBytes = 0;
				_totalFolderBytesProcessedInCurrentCab = 0;

				CabStream = null;
			}
			else  // Must be a temp stream
			{
				stream.Close();
				_tempStreams.Remove(stream);
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
					if (_fciHandle != IntPtr.Zero)
					{
						NativeMethods.FCI.Destroy(_fciHandle);
						_fciHandle = IntPtr.Zero;
					}
				}
			}
			finally
			{
				base.Dispose(disposing);
			}
		}

		private static NativeMethods.FCI.TCOMP GetCompressionType(CompressionLevel compLevel)
		{
			if (compLevel < CompressionLevel.Min)
				return NativeMethods.FCI.TCOMP.TYPE_NONE;
			if (compLevel > CompressionLevel.Max)
				compLevel = CompressionLevel.Max;

			const int lzxWindowMax = ((int) NativeMethods.FCI.TCOMP.LZX_WINDOW_HI >> (int) NativeMethods.FCI.TCOMP.SHIFT_LZX_WINDOW) -
			                         ((int) NativeMethods.FCI.TCOMP.LZX_WINDOW_LO >> (int) NativeMethods.FCI.TCOMP.SHIFT_LZX_WINDOW);
			var lzxWindow = lzxWindowMax*(compLevel - CompressionLevel.Min)/(CompressionLevel.Max - CompressionLevel.Min);

			return
				(NativeMethods.FCI.TCOMP)
					((int) NativeMethods.FCI.TCOMP.TYPE_LZX |
					 ((int) NativeMethods.FCI.TCOMP.LZX_WINDOW_LO + (lzxWindow << (int) NativeMethods.FCI.TCOMP.SHIFT_LZX_WINDOW)));
		}

		[SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands")]
		private void AddFile(
			string name,
			Stream stream,
			FileAttributes attributes,
			DateTime lastWriteTime,
			bool execute,
			CompressionLevel compLevel)
		{
			FileStream = stream;
			_fileAttributes = attributes &
				(FileAttributes.Archive | FileAttributes.Hidden | FileAttributes.ReadOnly | FileAttributes.System);
			_fileLastWriteTime = lastWriteTime;
			CurrentFileName = name;

			var tcomp = GetCompressionType(compLevel);

			var namePtr = IntPtr.Zero;
			try
			{
				var nameEncoding = Encoding.ASCII;
				if (Encoding.UTF8.GetByteCount(name) > name.Length)
				{
					nameEncoding = Encoding.UTF8;
					_fileAttributes |= FileAttributes.Normal;  // _A_NAME_IS_UTF
				}

				var nameBytes = nameEncoding.GetBytes(name);
				namePtr = Marshal.AllocHGlobal(nameBytes.Length + 1);
				Marshal.Copy(nameBytes, 0, namePtr, nameBytes.Length);
				Marshal.WriteByte(namePtr, nameBytes.Length, 0);

				Erf.Clear();
				NativeMethods.FCI.AddFile(
					_fciHandle,
					string.Empty,
					namePtr,
					execute,
					_fciGetNextCabinet,
					_fciCreateStatus,
					_fciGetOpenInfo,
					tcomp);
			}
			finally
			{
				if (namePtr != IntPtr.Zero)
					Marshal.FreeHGlobal(namePtr);
			}

			CheckError(false);
			FileStream = null;
			CurrentFileName = null;
		}

		private void FlushFolder()
		{
			Erf.Clear();
			NativeMethods.FCI.FlushFolder(_fciHandle, _fciGetNextCabinet, _fciCreateStatus);
			CheckError(false);
		}

		private void FlushCabinet()
		{
			Erf.Clear();
			NativeMethods.FCI.FlushCabinet(_fciHandle, false, _fciGetNextCabinet, _fciCreateStatus);
			CheckError(false);
		}

		private int CabGetOpenInfo(
			string path,
			out short date,
			out short time,
			out short attribs,
			out int err,
			IntPtr pv)
		{
			CompressionEngine.DateTimeToDOSDateAndTime(_fileLastWriteTime, out date, out time);
			attribs = (short) _fileAttributes;

			var stream = FileStream;
			FileStream = new DuplicateStream(stream);
			var streamHandle = StreamHandles.AllocHandle(stream);
			err = 0;
			return streamHandle;
		}

		private int CabFilePlaced(
			IntPtr pccab,
			string filePath,
			long fileSize,
			int continuation,
			IntPtr pv)
		{
			return 0;
		}

		private int CabGetNextCabinet(IntPtr pccab, uint prevCabSize, IntPtr pv)
		{
			var nextCcab = new NativeMethods.FCI.CCAB();
			Marshal.PtrToStructure(pccab, nextCcab);

			nextCcab.szDisk = string.Empty;
			nextCcab.szCab = _context.GetArchiveName(nextCcab.iCab);
			CabNumbers[nextCcab.szCab] = (short) nextCcab.iCab;
			NextCabinetName = nextCcab.szCab;

			Marshal.StructureToPtr(nextCcab, pccab, false);
			return 1;
		}

		private int CabCreateStatus(NativeMethods.FCI.STATUS typeStatus, uint cb1, uint cb2, IntPtr pv)
		{
			switch (typeStatus)
			{
				case NativeMethods.FCI.STATUS.FILE:
				{
					if (cb2 > 0 && CurrentFileBytesProcessed < CurrentFileTotalBytes)
					{
						if (CurrentFileBytesProcessed + cb2 > CurrentFileTotalBytes)
							cb2 = (uint)CurrentFileTotalBytes - (uint)CurrentFileBytesProcessed;
						CurrentFileBytesProcessed += cb2;
						FileBytesProcessed += cb2;

						OnProgress(ArchiveProgressType.PartialFile);
					}
					break;
				}

				case NativeMethods.FCI.STATUS.FOLDER:
				{
					if (cb1 == 0)
					{
						CurrentFolderTotalBytes = cb2 - _totalFolderBytesProcessedInCurrentCab;
						_totalFolderBytesProcessedInCurrentCab = cb2;
					}
					else if (CurrentFolderTotalBytes == 0)
						OnProgress(ArchiveProgressType.PartialArchive);
					break;
				}

				case NativeMethods.FCI.STATUS.CABINET:
					break;
			}
			return 0;
		}

		private int CabGetTempFile(IntPtr tempNamePtr, int tempNameSize, IntPtr pv)
		{
			string tempFileName = UseTempFiles
				? Path.GetFileName(Path.GetTempFileName())
				: TempStreamName;

			var tempNameBytes = Encoding.ASCII.GetBytes(tempFileName);
			if (tempNameBytes.Length >= tempNameSize)
				return -1;

			Marshal.Copy(tempNameBytes, 0, tempNamePtr, tempNameBytes.Length);
			Marshal.WriteByte(tempNamePtr, tempNameBytes.Length, 0);  // null-terminator
			return 1;
		}

		private int CabDeleteFile(string path, out int err, IntPtr pv)
		{
			try
			{
				// Deleting a temp file - don't bother if it is only a memory stream.
				if (path != TempStreamName)
				{
					path = Path.Combine(Path.GetTempPath(), path);
					File.Delete(path);
				}
			}
			catch (IOException)
			{
				// Failure to delete a temp file is not fatal.
			}
			err = 0;
			return 1;
		}
	}
}
