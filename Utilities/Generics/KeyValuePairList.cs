using System.Collections.Generic;

// ReSharper disable UnusedMember.Global
// ReSharper disable MemberCanBePrivate.Global
namespace IntegrateDrv.Utilities.Generics
{
	public class KeyValuePairList<TKey, TValue> : List<KeyValuePair<TKey, TValue>>
	{
		public bool ContainsKey(TKey key)
		{
			return (IndexOf(key) != -1);
		}

		public int IndexOf(TKey key)
		{
			for (var index = 0; index < Count; index++)
			{
				if (this[index].Key.Equals(key))
					return index;
			}

			return -1;
		}

		public TValue ValueOf(TKey key)
		{
			for (var index = 0; index < Count; index++)
			{
				if (this[index].Key.Equals(key))
					return this[index].Value;
			}

			return default(TValue);
		}

		public void Add(TKey key, TValue value)
		{
			Add(new KeyValuePair<TKey, TValue>(key, value));
		}

		public List<TKey> Keys
		{
			get
			{
				var result = new List<TKey>();
				foreach (var entity in this)
					result.Add(entity.Key);
				return result;
			}
		}

		public List<TValue> Values
		{
			get
			{
				var result = new List<TValue>();
				foreach (var entity in this)
					result.Add(entity.Value);
				return result;
			}
		}
	}
}
