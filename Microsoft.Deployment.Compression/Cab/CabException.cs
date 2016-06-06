//---------------------------------------------------------------------
// <copyright file="CabException.cs" company="Microsoft">
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
	using System.Resources;
	using System.Globalization;
	using System.Security.Permissions;
	using System.Runtime.Serialization;

	/// <summary>
	/// Exception class for cabinet operations.
	/// </summary>
	[Serializable]
	public class CabException : ArchiveException
	{
		private static ResourceManager _errorResources;
		private readonly int _error;
		private readonly int _errorCode;

		private CabException(int error, int errorCode, string message, Exception innerException)
			: base(message, innerException)
		{
			_error = error;
			_errorCode = errorCode;
		}

		internal CabException(int error, int errorCode, string message)
			: this(error, errorCode, message, null)
		{
		}

		/// <summary>
		/// Initializes a new instance of the CabException class with serialized data.
		/// </summary>
		/// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
		/// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
		protected CabException(SerializationInfo info, StreamingContext context) : base(info, context)
		{
			if (info == null)
				throw new ArgumentNullException("info");

			_error = info.GetInt32("cabError");
			_errorCode = info.GetInt32("cabErrorCode");
		}

		/// <summary>
		/// Gets the FCI or FDI cabinet engine error number.
		/// </summary>
		/// <value>A cabinet engine error number, or 0 if the exception was
		/// not related to a cabinet engine error number.</value>
		public int Error
		{
			get { return _error; }
		}

		/// <summary>
		/// Gets the Win32 error code.
		/// </summary>
		/// <value>A Win32 error code, or 0 if the exception was
		/// not related to a Win32 error.</value>
		public int ErrorCode
		{
			get { return _errorCode; }
		}

		internal static ResourceManager ErrorResources
		{
			get
			{
				if (_errorResources == null)
				{
					_errorResources = new ResourceManager(
						typeof (CabException).Namespace + ".Errors",
						typeof (CabException).Assembly);
				}
				return _errorResources;
			}
		}

		/// <summary>
		/// Sets the SerializationInfo with information about the exception.
		/// </summary>
		/// <param name="info">The SerializationInfo that holds the serialized object data about the exception being thrown.</param>
		/// <param name="context">The StreamingContext that contains contextual information about the source or destination.</param>
		[SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
		public override void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			if (info == null)
			{
				throw new ArgumentNullException("info");
			}

			info.AddValue("cabError", _error);
			info.AddValue("cabErrorCode", _errorCode);
			base.GetObjectData(info, context);
		}

		internal static string GetErrorMessage(int error, int errorCode, bool extracting)
		{
			const int fciErrorResourceOffset = 1000;
			const int fdiErrorResourceOffset = 2000;
			var resourceOffset = (extracting
				? fdiErrorResourceOffset
				: fciErrorResourceOffset);

			var msg = ErrorResources.GetString(
				(resourceOffset + error).ToString(CultureInfo.InvariantCulture.NumberFormat),
				CultureInfo.CurrentCulture);

			if (msg == null)
			{
				msg = ErrorResources.GetString(
					resourceOffset.ToString(CultureInfo.InvariantCulture.NumberFormat),
					CultureInfo.CurrentCulture);
			}

			if (errorCode != 0)
			{
				const string genericErrorResource = "1";
				var msg2 = ErrorResources.GetString(genericErrorResource, CultureInfo.CurrentCulture);
				msg = string.Format(CultureInfo.InvariantCulture, "{0} " + msg2, msg, errorCode);
			}
			return msg;
		}
	}
}