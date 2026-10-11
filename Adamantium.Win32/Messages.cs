using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Win32
{
   public static class Messages
   {
      [DllImport("user32.dll")]
      [return: MarshalAs(UnmanagedType.Bool)]
      public static extern bool PeekMessage(out Message lpMsg, IntPtr hWnd, WindowMessages wMsgFilterMin,
         WindowMessages wMsgFilterMax, PeekMessageFlag wRemoveMsg);

      [DllImport("user32.dll")]
      public static extern int GetMessage(out Message lpMsg, IntPtr hWnd, WindowMessages wMsgFilterMin,
         WindowMessages wMsgFilterMax);

      [DllImport("user32.dll")]
      public static extern bool TranslateMessage([In] ref Message lpMsg);

      [DllImport("user32.dll")]
      public static extern IntPtr DispatchMessage([In] ref Message lpmsg);

      [DllImport("user32.dll")]
      public static extern IntPtr SendMessage(IntPtr hWnd, UInt32 msg, IntPtr wParam, IntPtr lParam);

      [DllImport("user32.dll")]
      [return: MarshalAs(UnmanagedType.Bool)]
      public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

      [DllImport("user32.dll")]
      public static extern uint RegisterWindowMessage(string message);


      public static Vector2 PointFromLParam(IntPtr lParam)
      {
         var coordinates = (Environment.Is64BitProcess ? lParam.ToInt64() : lParam.ToInt32());
         int x = unchecked((short)coordinates);
         int y = unchecked((short)(coordinates >> 16));
         return new Vector2(x, y);
      }

      public static IntPtr LParamFromPoint(Vector2 p)
      {
         int value = ((ushort)p.Y << 16 | (ushort)p.X);
         return new IntPtr(value);
      }

      public static Int32 GetWheelDelta(IntPtr wParam)
      {
         var wheelDelta = Environment.Is64BitProcess ? wParam.ToInt64() : wParam.ToInt32();
         var delta = unchecked((short)(wheelDelta >> 16));
         return delta;
      }

      /// <summary>GET_POINTERID_WPARAM: the id of the pointer a WM_POINTER message is about.</summary>
      public static uint GetPointerId(IntPtr wParam) => (uint)(wParam.ToInt64() & 0xFFFF);

      public static MouseModifiers GetXButton(WindowMessages msg, IntPtr wParam)
      {
         switch (msg)
         {
            case WindowMessages.Xbuttondblclk:
            case WindowMessages.Xbuttondown:
            case WindowMessages.Xbuttonup:
               return GetMouseButtons(wParam, WordPart.HiWord);
            default:
               throw new ArgumentException("There is no corresponding Window message was paased");
         }
      }

      public static MouseModifiers GetMouseModifyKeys(WindowMessages msg, IntPtr wParam)
      {
         switch (msg)
         {
            case WindowMessages.Mousemove:
            case WindowMessages.LeftButtondblclk:
            case WindowMessages.RightButtondblclk:
            case WindowMessages.MiddleButtondblclk:
            case WindowMessages.LeftButtondown:
            case WindowMessages.RightButtondown:
            case WindowMessages.MiddleButtondown:
            case WindowMessages.LeftButtonup:
            case WindowMessages.RightButtonup:
            case WindowMessages.MiddleButtonup:
               return GetMouseButtons(wParam, WordPart.WholeWord);
            case WindowMessages.MouseWheel:
            case WindowMessages.MouseHwheel:
               return GetMouseButtons(wParam, WordPart.HiWord);
            case WindowMessages.Xbuttondblclk:
            case WindowMessages.Xbuttondown:
            case WindowMessages.Xbuttonup:
               return GetMouseButtons(wParam, WordPart.LoWord);
            default:
               throw new ArgumentException("There is no corresponding Window message was paased");
         }
      }

      private static MouseModifiers GetMouseButtons(IntPtr wParam, WordPart wordPart)
      {
         var modifiers = Environment.Is64BitProcess ? wParam.ToInt64() : wParam.ToInt32();
         MouseModifiers keys;
         switch (wordPart)
         {
            case WordPart.LoWord:
               keys = (MouseModifiers)(unchecked((short)modifiers));
               break;
            case WordPart.HiWord:
               keys = (MouseModifiers)(unchecked((short)modifiers) >> 16);
               break;
            default:
               keys = (MouseModifiers)modifiers;
               break;
         }
         return keys;
      }


      /// <summary>
      /// Returns keyboard key from wParam
      /// </summary>
      /// <param name="wParam"></param>
      /// <returns></returns>
      public static uint GetKey(IntPtr wParam)
      {
         return (uint)(Environment.Is64BitProcess ? wParam.ToInt64() : wParam.ToInt32());
      }

      public static KeyParameters GetKeyParameters(IntPtr lParam)
      {
         var state = Environment.Is64BitProcess ? lParam.ToInt64() : lParam.ToInt32();
         KeyParameters parameters = new KeyParameters();
         parameters.RepeatCount = (unchecked((short)state));
         parameters.ScanCode = (byte)(state >> 16);
         parameters.IsExtendedKey = Convert.ToBoolean((byte)(state >> 24) & 1);
         parameters.ContextCode = ((byte)(state >> 29) & 1);
         var previousState = (int)(state & 0x40000000);
         parameters.PreviousState = previousState == 0
            ? parameters.PreviousState = KeyStates.Up
            : parameters.PreviousState = KeyStates.Down;

         return parameters;
      }

      public static WindowActivation GetWindowActivationState(IntPtr wParam)
      {
         var state = Environment.Is64BitProcess ? wParam.ToInt64() : wParam.ToInt32();
         return (WindowActivation)(unchecked((short)state));
      }

      public static Char GetChar(IntPtr wParam)
      {
         var @char = Environment.Is64BitProcess ? wParam.ToInt64() : wParam.ToInt32();
         return (Char)@char;
      }
   }
}
