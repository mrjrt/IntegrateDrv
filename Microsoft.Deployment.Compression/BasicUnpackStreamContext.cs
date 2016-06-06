//---------------------------------------------------------------------
// <copyright file="BasicUnpackStreamContext.cs" company="Microsoft">
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

// ReSharper disable once CheckNamespace
namespace Microsoft.Deployment.Compression
{
	using System;
	using System.IO;
	using System.Diagnostics.CodeAnalysis;

	/// <summary>
	/// Stream context used to extract a single file from an archive into a memory stream.
	/// </summary>
	[SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
	public class BasicUnpackStreamContext : IUnpackStreamContext
	{
		private readonly Stream _archiveStream;

		/// <summary>
		/// Creates a new BasicExtractStreamContext that reads from the specified archive stream.
		/// </summary>
		/// <param name="archiveStream">Archive stream to read from.</param>
		public BasicUnpackStreamContext(Stream archiveStream)
		{
			_archiveStream = archiveStream;
		}

		/// <summary>
		/// Gets the stream for the extracted file, or null if no file was extracted.
		/// </summary>
		public Stream FileStream { get; private set; }

		/// <summary>
		/// Opens the archive stream for reading. Returns a DuplicateStream instance,
		/// so the stream may be virtually opened multiple times.
		/// </summary>
		/// <param name="archiveNumber">The archive number to open (ignored; 0 is assumed).</param>
		/// <param name="archiveName">The name of the archive being opened.</param>
		/// <param name="compressionEngine">Instance of the compression engine doing the operations.</param>
		/// <returns>A stream from which archive bytes are read.</returns>
		public Stream OpenArchiveReadStream(int archiveNumber, string archiveName, CompressionEngine compressionEngine)
		{
			return new DuplicateStream(_archiveStream);
		}

		/// <summary>
		/// Does *not* close the stream. The archive stream should be managed by
		/// the code that invokes the archive extraction.
		/// </summary>
		/// <param name="archiveNumber">The archive number of the stream to close.</param>
		/// <param name="archiveName">The name of the archive being closed.</param>
		/// <param name="stream">The stream being closed.</param>
		public void CloseArchiveReadStream(int archiveNumber, string archiveName, Stream stream)
		{
			// Do nothing.
		}

		/// <summary>
		/// Opens a stream for writing extracted file bytes. The returned stream is a MemoryStream
		/// instance, so the file is extracted straight into memory.
		/// </summary>
		/// <param name="path">Path of the file within the archive.</param>
		/// <param name="fileSize">The uncompressed size of the file to be extracted.</param>
		/// <param name="lastWriteTime">The last write time of the file.</param>
		/// <returns>A stream where extracted file bytes are to be written.</returns>
		public Stream OpenFileWriteStream(string path, long fileSize, DateTime lastWriteTime)
		{
			FileStream = new MemoryStream(new byte[fileSize], 0, (int) fileSize, true, true);
			return FileStream;
		}

		/// <summary>
		/// Does *not* close the file stream. The file stream is saved in memory so it can
		/// be read later.
		/// </summary>
		/// <param name="path">Path of the file within the archive.</param>
		/// <param name="stream">The file stream to be closed.</param>
		/// <param name="attributes">The attributes of the extracted file.</param>
		/// <param name="lastWriteTime">The last write time of the file.</param>
		public void CloseFileWriteStream(string path, Stream stream, FileAttributes attributes, DateTime lastWriteTime)
		{
			// Do nothing.
		}
	}
}