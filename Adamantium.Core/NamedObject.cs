// Copyright (c) 2010-2014 SharpDX - Alexandre Mutel
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace Adamantium.Core
{
   /// <summary>
   /// A lightweight base class providing possibility to assign Name and a Tag to class.
   /// </summary>
   [DataContract]
   public abstract class NamedObject : INamedObject
   {
      /// <summary>
      /// Occurs while this component is disposing and before it is disposed.
      /// </summary>
      //internal event EventHandler<EventArgs> Disposing;
      private string name = String.Empty;

      /// <summary>
      /// Gets or sets a value indicating whether the name of this instance is immutable.
      /// </summary>
      /// <value><c>true</c> if this instance is name immutable; otherwise, <c>false</c>.</value>
      public bool IsNameImmutable { get; set; }

      /// <summary>
      /// Initializes a new instance of the <see cref="NamedObject" /> class with a mutable name.
      /// </summary>
      protected NamedObject()
      {
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="NamedObject" /> class with an immutable name.
      /// </summary>
      /// <param name="name">The name.</param>
      protected NamedObject(string name, bool isImmutable = true)
      {
         this.name = name;
         IsNameImmutable = isImmutable;
      }

      /// <summary>
      /// Gets the name of this component.
      /// </summary>
      /// <value>The name.</value>
      [DefaultValue("")]
      [DataMember(Order = 1)]
      public string Name
      {
         get => name;
         set
         {
            if (IsNameImmutable)
               throw new ArgumentException("Name property is immutable for this instance", nameof(value));
            name = value;
         }
      }

      public bool HasName => !string.IsNullOrEmpty(Name);

      /// <summary>
      /// Gets or sets the tag associated to this object.
      /// </summary>
      /// <value>The tag.</value>
      [DefaultValue(null)]
      [DataMember(Order = 2, EmitDefaultValue = false)]
      public object Tag { get; set; }
   }
}