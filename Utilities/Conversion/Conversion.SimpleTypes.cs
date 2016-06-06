using System;

namespace IntegrateDrv.Utilities.Conversion
{
	public static class Conversion
	{
		public static int ToInt32(object obj)
		{
			return ToInt32(obj, 0);
		}

		public static int ToInt32(object obj, int defaultValue)
		{
			var result = defaultValue;
			if (obj != null)
			{
				try
				{
					result = Convert.ToInt32(obj);
				}
				catch
				{}
			}
			return result;
		}
	}
}
