namespace IntegrateDrv.DeviceService
{
	public class BaseDeviceService
	{
		private readonly string _deviceDescription = string.Empty;
		private readonly string _serviceName = string.Empty;
		private readonly string _serviceDisplayName = string.Empty;
		private readonly string _serviceGroup;
		private readonly int _serviceType;
		private readonly int _errorControl;
		private readonly string _fileName = string.Empty;
		private readonly string _imagePath = string.Empty;

		public BaseDeviceService(string deviceDescription, string serviceName, string serviceDisplayName, string serviceGroup,
			int serviceType, int errorControl, string fileName, string imagePath)
		{
			_deviceDescription = deviceDescription;
			_serviceName = serviceName;
			_serviceDisplayName = serviceDisplayName;
			_serviceGroup = serviceGroup;
			_serviceType = serviceType;
			_errorControl = errorControl;
			_fileName = fileName;
			_imagePath = imagePath;
		}

		public string DeviceDescription
		{
			get { return _deviceDescription; }
		}

		public string ServiceName
		{
			get { return _serviceName; }
		}

		public string ServiceDisplayName
		{
			get { return _serviceDisplayName; }
		}

		public string ServiceGroup
		{
			get { return _serviceGroup; }
		}

		public int ServiceType
		{
			get { return _serviceType; }
		}

		public int ErrorControl
		{
			get { return _errorControl; }
		}

		/// <summary>
		/// File name of the service executable image (.sys file)
		/// </summary>
		public string FileName
		{
			get { return _fileName; }
		}

		// this will only be used for GUI-mode
		public string ImagePath
		{
			get { return _imagePath; }
		}

		// Text-mode setup will always initialize services based on the values stored under
		// Services\serviceName, where serviceName is the service file name without the .sys extension.
		// if serviceName != file name without the .sys extension, and we want the service to work properly in text mode,
		// we can either change the service name or change the filename

		public string TextModeFileName
		{
			get { return _serviceName + ".sys"; }
		}
	}
}
