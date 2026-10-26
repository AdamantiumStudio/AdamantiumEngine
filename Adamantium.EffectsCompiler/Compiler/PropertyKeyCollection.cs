using System;
using System.Collections.Generic;
using Adamantium.Core;

namespace Adamantium.EffectsCompiler
{
   public class PropertyKeyCollection : Dictionary<PropertyKey, Object>
   {
      /// <summary>
      /// Initializes a new instance of the <see cref="PropertyKeyCollection"/> class.
      /// </summary>
      public PropertyKeyCollection()
      {
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="PropertyKeyCollection" /> class that is empty, has the specified initial capacity, and uses the default equality comparer for the key type.
      /// </summary>
      /// <param name="capacity">The initial number of elements that the <see cref="PropertyKeyCollection" /> can contain.</param>
      public PropertyKeyCollection(int capacity)
         : base(capacity)
      {
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="PropertyKeyCollection"/> class.
      /// </summary>
      /// <param name="dictionary">The dictionary.</param>
      public PropertyKeyCollection(IDictionary<PropertyKey, Object> dictionary)
         : base(dictionary)
      {
      }

      public void SetProperty<T>(PropertyKey<T> key, T value)
      {
         if (typeof(T).IsEnum)
         {
            Add(key, Convert.ChangeType(value, Enum.GetUnderlyingType(typeof(T))));
         }
         else
         {
            Add(key, value);
         }
      }

      public bool ContainsKey<T>(PropertyKey<T> key)
      {
         return base.ContainsKey(key);
      }

      public T GetProperty<T>(PropertyKey<T> key)
      {
         return TryGetValue(key, out var value) ? typeof(T).IsEnum ? (T)Enum.ToObject(typeof(T), value) : (T)value : default(T);
      }

      public virtual PropertyKeyCollection Clone()
      {
         return (PropertyKeyCollection)MemberwiseClone();
      }

   }
}
