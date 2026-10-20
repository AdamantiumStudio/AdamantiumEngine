using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Adamantium.Mathematics
{
    public class Colors
    {
        private static readonly Dictionary<string, Color> ColorsMap;

        static Colors()
        {
            // CASE-INSENSITIVE, because a color's name is a name and not a symbol. Every format that writes one -
            // CSS, SVG, a theme file typed by hand - spells it however it likes: "tomato", "Tomato", "TOMATO" are one
            // color to all of them, and looked up exactly, a drawing that named "blue" came back with no stroke at
            // all rather than a blue one.
            ColorsMap = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            ColorsMap["Zero"] = Color.FromBgra(0x00000000);
            ColorsMap["Transparent"] = Color.FromBgra(0x00000000);
            ColorsMap["AliceBlue"] = Color.FromBgra(0xFFF0F8FF);
            ColorsMap["AntiqueWhite"] = Color.FromBgra(0xFFFAEBD7);
            ColorsMap["Aqua"] = Color.FromBgra(0xFF00FFFF);
            ColorsMap["Aquamarine"] = Color.FromBgra(0xFF7FFFD4);
            ColorsMap["Azure"] = Color.FromBgra(0xFFF0FFFF);
            ColorsMap["Beige"] = Color.FromBgra(0xFFF5F5DC);
            ColorsMap["Bisque"] = Color.FromBgra(0xFFFFE4C4);
            ColorsMap["Black"] = Color.FromBgra(0xFF000000);
            ColorsMap["BlanchedAlmond"] = Color.FromBgra(0xFFFFEBCD);
            ColorsMap["Blue"] = Color.FromBgra(0xFF0000FF);
            ColorsMap["BlueViolet"] = Color.FromBgra(0xFF8A2BE2);
            ColorsMap["Brown"] = Color.FromBgra(0xFFA52A2A);
            ColorsMap["BurlyWood"] = Color.FromBgra(0xFFDEB887);
            ColorsMap["CadetBlue"] = Color.FromBgra(0xFF5F9EA0);
            ColorsMap["Chartreuse"] = Color.FromBgra(0xFF7FFF00);
            ColorsMap["Chocolate"] = Color.FromBgra(0xFFD2691E);
            ColorsMap["Coral"] = Color.FromBgra(0xFFFF7F50);
            ColorsMap["CornflowerBlue"] = Color.FromBgra(0xFF6495ED);
            ColorsMap["Cornsilk"] = Color.FromBgra(0xFFFFF8DC);
            ColorsMap["Crimson"] = Color.FromBgra(0xFFDC143C);
            ColorsMap["Cyan"] = Color.FromBgra(0xFF00FFFF);
            ColorsMap["DarkBlue"] = Color.FromBgra(0xFF00008B);
            ColorsMap["DarkCyan"] = Color.FromBgra(0xFF008B8B);
            ColorsMap["DarkGoldenrod"] = Color.FromBgra(0xFFB8860B);
            ColorsMap["DarkGray"] = Color.FromBgra(0xFFA9A9A9);
            ColorsMap["DarkGreen"] = Color.FromBgra(0xFF006400);
            ColorsMap["DarkKhaki"] = Color.FromBgra(0xFFBDB76B);
            ColorsMap["DarkMagenta"] = Color.FromBgra(0xFF8B008B);
            ColorsMap["DarkOliveGreen"] = Color.FromBgra(0xFF556B2F);
            ColorsMap["DarkOrange"] = Color.FromBgra(0xFFFF8C00);
            ColorsMap["DarkOrchid"] = Color.FromBgra(0xFF9932CC);
            ColorsMap["DarkRed"] = Color.FromBgra(0xFF8B0000);
            ColorsMap["DarkSalmon"] = Color.FromBgra(0xFFE9967A);
            ColorsMap["DarkSeaGreen"] = Color.FromBgra(0xFF8FBC8B);
            ColorsMap["DarkSlateBlue"] = Color.FromBgra(0xFF483D8B);
            ColorsMap["DarkSlateGray"] = Color.FromBgra(0xFF2F4F4F);
            ColorsMap["DarkTurquoise"] = Color.FromBgra(0xFF00CED1);
            ColorsMap["DarkViolet"] = Color.FromBgra(0xFF9400D3);
            ColorsMap["DeepPink"] = Color.FromBgra(0xFFFF1493);
            ColorsMap["DeepSkyBlue"] = Color.FromBgra(0xFF00BFFF);
            ColorsMap["DimGray"] = Color.FromBgra(0xFF696969);
            ColorsMap["DodgerBlue"] = Color.FromBgra(0xFF1E90FF);
            ColorsMap["Firebrick"] = Color.FromBgra(0xFFB22222);
            ColorsMap["FloralWhite"] = Color.FromBgra(0xFFFFFAF0);
            ColorsMap["ForestGreen"] = Color.FromBgra(0xFF228B22);
            ColorsMap["Fuchsia"] = Color.FromBgra(0xFFFF00FF);
            ColorsMap["Gainsboro"] = Color.FromBgra(0xFFDCDCDC);
            ColorsMap["GhostWhite"] = Color.FromBgra(0xFFF8F8FF);
            ColorsMap["Gold"] = Color.FromBgra(0xFFFFD700);
            ColorsMap["Goldenrod"] = Color.FromBgra(0xFFDAA520);
            ColorsMap["Gray"] = Color.FromBgra(0xFF808080);
            ColorsMap["Green"] = Color.FromBgra(0xFF008000);
            ColorsMap["GreenYellow"] = Color.FromBgra(0xFFADFF2F);
            ColorsMap["Honeydew"] = Color.FromBgra(0xFFF0FFF0);
            ColorsMap["HotPink"] = Color.FromBgra(0xFFFF69B4);
            ColorsMap["IndianRed"] = Color.FromBgra(0xFFCD5C5C);
            ColorsMap["Indigo"] = Color.FromBgra(0xFF4B0082);
            ColorsMap["Ivory"] = Color.FromBgra(0xFFFFFFF0);
            ColorsMap["Khaki"] = Color.FromBgra(0xFFF0E68C);
            ColorsMap["Lavender"] = Color.FromBgra(0xFFE6E6FA);
            ColorsMap["LavenderBlush"] = Color.FromBgra(0xFFFFF0F5);
            ColorsMap["LawnGreen"] = Color.FromBgra(0xFF7CFC00);
            ColorsMap["LemonChiffon"] = Color.FromBgra(0xFFFFFACD);
            ColorsMap["LightBlue"] = Color.FromBgra(0xFFADD8E6);
            ColorsMap["LightCoral"] = Color.FromBgra(0xFFF08080);
            ColorsMap["LightCyan"] = Color.FromBgra(0xFFE0FFFF);
            ColorsMap["LightGoldenrodYellow"] = Color.FromBgra(0xFFFAFAD2);
            ColorsMap["LightGray"] = Color.FromBgra(0xFFD3D3D3);
            ColorsMap["LightGreen"] = Color.FromBgra(0xFF90EE90);
            ColorsMap["LightPink"] = Color.FromBgra(0xFFFFB6C1);
            ColorsMap["LightSalmon"] = Color.FromBgra(0xFFFFA07A);
            ColorsMap["LightSeaGreen"] = Color.FromBgra(0xFF20B2AA);
            ColorsMap["LightSkyBlue"] = Color.FromBgra(0xFF87CEFA);
            ColorsMap["LightSlateGray"] = Color.FromBgra(0xFF778899);
            ColorsMap["LightSteelBlue"] = Color.FromBgra(0xFFB0C4DE);
            ColorsMap["LightYellow"] = Color.FromBgra(0xFFFFFFE0);
            ColorsMap["Lime"] = Color.FromBgra(0xFF00FF00);
            ColorsMap["LimeGreen"] = Color.FromBgra(0xFF32CD32);
            ColorsMap["Linen"] = Color.FromBgra(0xFFFAF0E6);
            ColorsMap["Magenta"] = Color.FromBgra(0xFFFF00FF);
            ColorsMap["Maroon"] = Color.FromBgra(0xFF800000);
            ColorsMap["MediumAquamarine"] = Color.FromBgra(0xFF66CDAA);
            ColorsMap["MediumBlue"] = Color.FromBgra(0xFF0000CD);
            ColorsMap["MediumOrchid"] = Color.FromBgra(0xFFBA55D3);
            ColorsMap["MediumPurple"] = Color.FromBgra(0xFF9370DB);
            ColorsMap["MediumSeaGreen"] = Color.FromBgra(0xFF3CB371);
            ColorsMap["MediumSlateBlue"] = Color.FromBgra(0xFF7B68EE);
            ColorsMap["MediumSpringGreen"] = Color.FromBgra(0xFF00FA9A);
            ColorsMap["MediumTurquoise"] = Color.FromBgra(0xFF48D1CC);
            ColorsMap["MediumVioletRed"] = Color.FromBgra(0xFFC71585);
            ColorsMap["MidnightBlue"] = Color.FromBgra(0xFF191970);
            ColorsMap["MintCream"] = Color.FromBgra(0xFFF5FFFA);
            ColorsMap["MistyRose"] = Color.FromBgra(0xFFFFE4E1);
            ColorsMap["Moccasin"] = Color.FromBgra(0xFFFFE4B5);
            ColorsMap["NavajoWhite"] = Color.FromBgra(0xFFFFDEAD);
            ColorsMap["Navy"] = Color.FromBgra(0xFF000080);
            ColorsMap["OldLace"] = Color.FromBgra(0xFFFDF5E6);
            ColorsMap["Olive"] = Color.FromBgra(0xFF808000);
            ColorsMap["OliveDrab"] = Color.FromBgra(0xFF6B8E23);
            ColorsMap["Orange"] = Color.FromBgra(0xFFFFA500);
            ColorsMap["OrangeRed"] = Color.FromBgra(0xFFFF4500);
            ColorsMap["Orchid"] = Color.FromBgra(0xFFDA70D6);
            ColorsMap["PaleGoldenrod"] = Color.FromBgra(0xFFEEE8AA);
            ColorsMap["PaleGreen"] = Color.FromBgra(0xFF98FB98);
            ColorsMap["PaleTurquoise"] = Color.FromBgra(0xFFAFEEEE);
            ColorsMap["PaleVioletRed"] = Color.FromBgra(0xFFDB7093);
            ColorsMap["PapayaWhip"] = Color.FromBgra(0xFFFFEFD5);
            ColorsMap["PeachPuff"] = Color.FromBgra(0xFFFFDAB9);
            ColorsMap["Peru"] = Color.FromBgra(0xFFCD853F);
            ColorsMap["Pink"] = Color.FromBgra(0xFFFFC0CB);
            ColorsMap["Plum"] = Color.FromBgra(0xFFDDA0DD);
            ColorsMap["PowderBlue"] = Color.FromBgra(0xFFB0E0E6);
            ColorsMap["Purple"] = Color.FromBgra(0xFF800080);
            ColorsMap["Red"] = Color.FromBgra(0xFFFF0000);
            ColorsMap["RosyBrown"] = Color.FromBgra(0xFFBC8F8F);
            ColorsMap["RoyalBlue"] = Color.FromBgra(0xFF4169E1);
            ColorsMap["SaddleBrown"] = Color.FromBgra(0xFF8B4513);
            ColorsMap["Salmon"] = Color.FromBgra(0xFFFA8072);
            ColorsMap["SandyBrown"] = Color.FromBgra(0xFFF4A460);
            ColorsMap["SeaGreen"] = Color.FromBgra(0xFF2E8B57);
            ColorsMap["SeaShell"] = Color.FromBgra(0xFFFFF5EE);
            ColorsMap["Sienna"] = Color.FromBgra(0xFFA0522D);
            ColorsMap["Silver"] = Color.FromBgra(0xFFC0C0C0);
            ColorsMap["SkyBlue"] = Color.FromBgra(0xFF87CEEB);
            ColorsMap["SlateBlue"] = Color.FromBgra(0xFF6A5ACD);
            ColorsMap["SlateGray"] = Color.FromBgra(0xFF708090);
            ColorsMap["Snow"] = Color.FromBgra(0xFFFFFAFA);
            ColorsMap["SpringGreen"] = Color.FromBgra(0xFF00FF7F);
            ColorsMap["SteelBlue"] = Color.FromBgra(0xFF4682B4);
            ColorsMap["Tan"] = Color.FromBgra(0xFFD2B48C);
            ColorsMap["Teal"] = Color.FromBgra(0xFF008080);
            ColorsMap["Thistle"] = Color.FromBgra(0xFFD8BFD8);
            ColorsMap["Tomato"] = Color.FromBgra(0xFFFF6347);
            ColorsMap["Turquoise"] = Color.FromBgra(0xFF40E0D0);
            ColorsMap["Violet"] = Color.FromBgra(0xFFEE82EE);
            ColorsMap["Wheat"] = Color.FromBgra(0xFFF5DEB3);
            ColorsMap["White"] = Color.FromBgra(0xFFFFFFFF);
            ColorsMap["WhiteSmoke"] = Color.FromBgra(0xFFF5F5F5);
            ColorsMap["Yellow"] = Color.FromBgra(0xFFFFFF00);
            ColorsMap["YellowGreen"] = Color.FromBgra(0xFF9ACD32);
        }

        /// <summary>The color <paramref name="name"/> stands for, as <see cref="TryParse"/> reads it.</summary>
        /// <exception cref="FormatException">It is no color.</exception>
        public static Color Get(string name) =>
            TryParse(name, out var color) ? color : throw new FormatException($"'{name}' is not a color.");

        /// <summary>A color as markup writes it: a name in any case, or <c>#RGB</c>, <c>#ARGB</c>, <c>#RRGGBB</c> or
        /// <c>#AARRGGBB</c> - the alpha first, a short form's digits each doubled; false for anything else.</summary>
        public static bool TryParse(string text, out Color color)
        {
            color = default;
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (text[0] != '#')
            {
                return TryGetNamed(text, out color);
            }

            var digits = text.Substring(1);
            if (digits.Length is not (3 or 4 or 6 or 8))
            {
                return false;
            }

            uint argb = 0;
            foreach (var digit in digits)
            {
                var value = digit is >= '0' and <= '9' ? digit - '0'
                    : digit is >= 'a' and <= 'f' ? digit - 'a' + 10
                    : digit is >= 'A' and <= 'F' ? digit - 'A' + 10
                    : -1;
                if (value < 0)
                {
                    return false;
                }

                argb = digits.Length <= 4 ? argb << 8 | (uint)(value * 17) : argb << 4 | (uint)value;
            }

            if (digits.Length is 3 or 6)
            {
                argb |= 0xFF000000;
            }

            color = Color.FromBgra(argb);
            return true;
        }

        /// <summary>The color a name stands for, in any case ("Tomato", "tomato"); false for a name it does not know.</summary>
        public static bool TryGetNamed(string name, out Color color)
        {
            return ColorsMap.TryGetValue(name ?? string.Empty, out color);
        }

        /// <summary>
        /// Zero color.
        /// </summary>
        public static Color Zero => ColorsMap["Zero"];

        /// <summary>
        /// Transparent color.
        /// </summary>
        public static Color Transparent => ColorsMap["Transparent"];

        /// <summary>
        /// AliceBlue color.
        /// </summary>
        public static readonly Color AliceBlue = Color.FromBgra(0xFFF0F8FF);

        /// <summary>
        /// AntiqueWhite color.
        /// </summary>
        public static readonly Color AntiqueWhite = Color.FromBgra(0xFFFAEBD7);

        /// <summary>
        /// Aqua color.
        /// </summary>
        public static readonly Color Aqua = Color.FromBgra(0xFF00FFFF);

        /// <summary>
        /// Aquamarine color.
        /// </summary>
        public static readonly Color Aquamarine = Color.FromBgra(0xFF7FFFD4);

        /// <summary>
        /// Azure color.
        /// </summary>
        public static readonly Color Azure = Color.FromBgra(0xFFF0FFFF);

        /// <summary>
        /// Beige color.
        /// </summary>
        public static readonly Color Beige = Color.FromBgra(0xFFF5F5DC);

        /// <summary>
        /// Bisque color.
        /// </summary>
        public static readonly Color Bisque = Color.FromBgra(0xFFFFE4C4);

        /// <summary>
        /// Black color.
        /// </summary>
        public static readonly Color Black = Color.FromBgra(0xFF000000);

        /// <summary>
        /// BlanchedAlmond color.
        /// </summary>
        public static readonly Color BlanchedAlmond = Color.FromBgra(0xFFFFEBCD);

        /// <summary>
        /// Blue color.
        /// </summary>
        public static readonly Color Blue = Color.FromBgra(0xFF0000FF);

        /// <summary>
        /// BlueViolet color.
        /// </summary>
        public static readonly Color BlueViolet = Color.FromBgra(0xFF8A2BE2);

        /// <summary>
        /// Brown color.
        /// </summary>
        public static readonly Color Brown = Color.FromBgra(0xFFA52A2A);

        /// <summary>
        /// BurlyWood color.
        /// </summary>
        public static readonly Color BurlyWood = Color.FromBgra(0xFFDEB887);

        /// <summary>
        /// CadetBlue color.
        /// </summary>
        public static readonly Color CadetBlue = Color.FromBgra(0xFF5F9EA0);

        /// <summary>
        /// Chartreuse color.
        /// </summary>
        public static readonly Color Chartreuse = Color.FromBgra(0xFF7FFF00);

        /// <summary>
        /// Chocolate color.
        /// </summary>
        public static readonly Color Chocolate = Color.FromBgra(0xFFD2691E);

        /// <summary>
        /// Coral color.
        /// </summary>
        public static readonly Color Coral = Color.FromBgra(0xFFFF7F50);

        /// <summary>
        /// CornflowerBlue color.
        /// </summary>
        public static readonly Color CornflowerBlue = Color.FromBgra(0xFF6495ED);

        /// <summary>
        /// Cornsilk color.
        /// </summary>
        public static readonly Color Cornsilk = Color.FromBgra(0xFFFFF8DC);

        /// <summary>
        /// Crimson color.
        /// </summary>
        public static readonly Color Crimson = Color.FromBgra(0xFFDC143C);

        /// <summary>
        /// Cyan color.
        /// </summary>
        public static readonly Color Cyan = Color.FromBgra(0xFF00FFFF);

        /// <summary>
        /// DarkBlue color.
        /// </summary>
        public static readonly Color DarkBlue = Color.FromBgra(0xFF00008B);

        /// <summary>
        /// DarkCyan color.
        /// </summary>
        public static readonly Color DarkCyan = Color.FromBgra(0xFF008B8B);

        /// <summary>
        /// DarkGoldenrod color.
        /// </summary>
        public static readonly Color DarkGoldenrod = Color.FromBgra(0xFFB8860B);

        /// <summary>
        /// DarkGray color.
        /// </summary>
        public static readonly Color DarkGray = Color.FromBgra(0xFFA9A9A9);

        /// <summary>
        /// DarkGreen color.
        /// </summary>
        public static readonly Color DarkGreen = Color.FromBgra(0xFF006400);

        /// <summary>
        /// DarkKhaki color.
        /// </summary>
        public static readonly Color DarkKhaki = Color.FromBgra(0xFFBDB76B);

        /// <summary>
        /// DarkMagenta color.
        /// </summary>
        public static readonly Color DarkMagenta = Color.FromBgra(0xFF8B008B);

        /// <summary>
        /// DarkOliveGreen color.
        /// </summary>
        public static readonly Color DarkOliveGreen = Color.FromBgra(0xFF556B2F);

        /// <summary>
        /// DarkOrange color.
        /// </summary>
        public static readonly Color DarkOrange = Color.FromBgra(0xFFFF8C00);

        /// <summary>
        /// DarkOrchid color.
        /// </summary>
        public static readonly Color DarkOrchid = Color.FromBgra(0xFF9932CC);

        /// <summary>
        /// DarkRed color.
        /// </summary>
        public static readonly Color DarkRed = Color.FromBgra(0xFF8B0000);

        /// <summary>
        /// DarkSalmon color.
        /// </summary>
        public static readonly Color DarkSalmon = Color.FromBgra(0xFFE9967A);

        /// <summary>
        /// DarkSeaGreen color.
        /// </summary>
        public static readonly Color DarkSeaGreen = Color.FromBgra(0xFF8FBC8B);

        /// <summary>
        /// DarkSlateBlue color.
        /// </summary>
        public static readonly Color DarkSlateBlue = Color.FromBgra(0xFF483D8B);

        /// <summary>
        /// DarkSlateGray color.
        /// </summary>
        public static readonly Color DarkSlateGray = Color.FromBgra(0xFF2F4F4F);

        /// <summary>
        /// DarkTurquoise color.
        /// </summary>
        public static readonly Color DarkTurquoise = Color.FromBgra(0xFF00CED1);

        /// <summary>
        /// DarkViolet color.
        /// </summary>
        public static readonly Color DarkViolet = Color.FromBgra(0xFF9400D3);

        /// <summary>
        /// DeepPink color.
        /// </summary>
        public static readonly Color DeepPink = Color.FromBgra(0xFFFF1493);

        /// <summary>
        /// DeepSkyBlue color.
        /// </summary>
        public static readonly Color DeepSkyBlue = Color.FromBgra(0xFF00BFFF);

        /// <summary>
        /// DimGray color.
        /// </summary>
        public static readonly Color DimGray = Color.FromBgra(0xFF696969);

        /// <summary>
        /// DodgerBlue color.
        /// </summary>
        public static readonly Color DodgerBlue = Color.FromBgra(0xFF1E90FF);

        /// <summary>
        /// Firebrick color.
        /// </summary>
        public static readonly Color Firebrick = Color.FromBgra(0xFFB22222);

        /// <summary>
        /// FloralWhite color.
        /// </summary>
        public static readonly Color FloralWhite = Color.FromBgra(0xFFFFFAF0);

        /// <summary>
        /// ForestGreen color.
        /// </summary>
        public static readonly Color ForestGreen = Color.FromBgra(0xFF228B22);

        /// <summary>
        /// Fuchsia color.
        /// </summary>
        public static readonly Color Fuchsia = Color.FromBgra(0xFFFF00FF);

        /// <summary>
        /// Gainsboro color.
        /// </summary>
        public static readonly Color Gainsboro = Color.FromBgra(0xFFDCDCDC);

        /// <summary>
        /// GhostWhite color.
        /// </summary>
        public static readonly Color GhostWhite = Color.FromBgra(0xFFF8F8FF);

        /// <summary>
        /// Gold color.
        /// </summary>
        public static readonly Color Gold = Color.FromBgra(0xFFFFD700);

        /// <summary>
        /// Goldenrod color.
        /// </summary>
        public static readonly Color Goldenrod = Color.FromBgra(0xFFDAA520);

        /// <summary>
        /// Gray color.
        /// </summary>
        public static readonly Color Gray = Color.FromBgra(0xFF808080);

        /// <summary>
        /// Green color.
        /// </summary>
        public static readonly Color Green = Color.FromBgra(0xFF008000);

        /// <summary>
        /// GreenYellow color.
        /// </summary>
        public static readonly Color GreenYellow = Color.FromBgra(0xFFADFF2F);

        /// <summary>
        /// Honeydew color.
        /// </summary>
        public static readonly Color Honeydew = Color.FromBgra(0xFFF0FFF0);

        /// <summary>
        /// HotPink color.
        /// </summary>
        public static readonly Color HotPink = Color.FromBgra(0xFFFF69B4);

        /// <summary>
        /// IndianRed color.
        /// </summary>
        public static readonly Color IndianRed = Color.FromBgra(0xFFCD5C5C);

        /// <summary>
        /// Indigo color.
        /// </summary>
        public static readonly Color Indigo = Color.FromBgra(0xFF4B0082);

        /// <summary>
        /// Ivory color.
        /// </summary>
        public static readonly Color Ivory = Color.FromBgra(0xFFFFFFF0);

        /// <summary>
        /// Khaki color.
        /// </summary>
        public static readonly Color Khaki = Color.FromBgra(0xFFF0E68C);

        /// <summary>
        /// Lavender color.
        /// </summary>
        public static readonly Color Lavender = Color.FromBgra(0xFFE6E6FA);

        /// <summary>
        /// LavenderBlush color.
        /// </summary>
        public static readonly Color LavenderBlush = Color.FromBgra(0xFFFFF0F5);

        /// <summary>
        /// LawnGreen color.
        /// </summary>
        public static readonly Color LawnGreen = Color.FromBgra(0xFF7CFC00);

        /// <summary>
        /// LemonChiffon color.
        /// </summary>
        public static readonly Color LemonChiffon = Color.FromBgra(0xFFFFFACD);

        /// <summary>
        /// LightBlue color.
        /// </summary>
        public static readonly Color LightBlue = Color.FromBgra(0xFFADD8E6);

        /// <summary>
        /// LightCoral color.
        /// </summary>
        public static readonly Color LightCoral = Color.FromBgra(0xFFF08080);

        /// <summary>
        /// LightCyan color.
        /// </summary>
        public static readonly Color LightCyan = Color.FromBgra(0xFFE0FFFF);

        /// <summary>
        /// LightGoldenrodYellow color.
        /// </summary>
        public static readonly Color LightGoldenrodYellow = Color.FromBgra(0xFFFAFAD2);

        /// <summary>
        /// LightGray color.
        /// </summary>
        public static readonly Color LightGray = Color.FromBgra(0xFFD3D3D3);

        /// <summary>
        /// LightGreen color.
        /// </summary>
        public static readonly Color LightGreen = Color.FromBgra(0xFF90EE90);

        /// <summary>
        /// LightPink color.
        /// </summary>
        public static readonly Color LightPink = Color.FromBgra(0xFFFFB6C1);

        /// <summary>
        /// LightSalmon color.
        /// </summary>
        public static readonly Color LightSalmon = Color.FromBgra(0xFFFFA07A);

        /// <summary>
        /// LightSeaGreen color.
        /// </summary>
        public static readonly Color LightSeaGreen = Color.FromBgra(0xFF20B2AA);

        /// <summary>
        /// LightSkyBlue color.
        /// </summary>
        public static readonly Color LightSkyBlue = Color.FromBgra(0xFF87CEFA);

        /// <summary>
        /// LightSlateGray color.
        /// </summary>
        public static readonly Color LightSlateGray = Color.FromBgra(0xFF778899);

        /// <summary>
        /// LightSteelBlue color.
        /// </summary>
        public static readonly Color LightSteelBlue = Color.FromBgra(0xFFB0C4DE);

        /// <summary>
        /// LightYellow color.
        /// </summary>
        public static readonly Color LightYellow = Color.FromBgra(0xFFFFFFE0);

        /// <summary>
        /// Lime color.
        /// </summary>
        public static readonly Color Lime = Color.FromBgra(0xFF00FF00);

        /// <summary>
        /// LimeGreen color.
        /// </summary>
        public static readonly Color LimeGreen = Color.FromBgra(0xFF32CD32);

        /// <summary>
        /// Linen color.
        /// </summary>
        public static readonly Color Linen = Color.FromBgra(0xFFFAF0E6);

        /// <summary>
        /// Magenta color.
        /// </summary>
        public static readonly Color Magenta = Color.FromBgra(0xFFFF00FF);

        /// <summary>
        /// Maroon color.
        /// </summary>
        public static readonly Color Maroon = Color.FromBgra(0xFF800000);

        /// <summary>
        /// MediumAquamarine color.
        /// </summary>
        public static readonly Color MediumAquamarine = Color.FromBgra(0xFF66CDAA);

        /// <summary>
        /// MediumBlue color.
        /// </summary>
        public static readonly Color MediumBlue = Color.FromBgra(0xFF0000CD);

        /// <summary>
        /// MediumOrchid color.
        /// </summary>
        public static readonly Color MediumOrchid = Color.FromBgra(0xFFBA55D3);

        /// <summary>
        /// MediumPurple color.
        /// </summary>
        public static readonly Color MediumPurple = Color.FromBgra(0xFF9370DB);

        /// <summary>
        /// MediumSeaGreen color.
        /// </summary>
        public static readonly Color MediumSeaGreen = Color.FromBgra(0xFF3CB371);

        /// <summary>
        /// MediumSlateBlue color.
        /// </summary>
        public static readonly Color MediumSlateBlue = Color.FromBgra(0xFF7B68EE);

        /// <summary>
        /// MediumSpringGreen color.
        /// </summary>
        public static readonly Color MediumSpringGreen = Color.FromBgra(0xFF00FA9A);

        /// <summary>
        /// MediumTurquoise color.
        /// </summary>
        public static readonly Color MediumTurquoise = Color.FromBgra(0xFF48D1CC);

        /// <summary>
        /// MediumVioletRed color.
        /// </summary>
        public static readonly Color MediumVioletRed = Color.FromBgra(0xFFC71585);

        /// <summary>
        /// MidnightBlue color.
        /// </summary>
        public static readonly Color MidnightBlue = Color.FromBgra(0xFF191970);

        /// <summary>
        /// MintCream color.
        /// </summary>
        public static readonly Color MintCream = Color.FromBgra(0xFFF5FFFA);

        /// <summary>
        /// MistyRose color.
        /// </summary>
        public static readonly Color MistyRose = Color.FromBgra(0xFFFFE4E1);

        /// <summary>
        /// Moccasin color.
        /// </summary>
        public static readonly Color Moccasin = Color.FromBgra(0xFFFFE4B5);

        /// <summary>
        /// NavajoWhite color.
        /// </summary>
        public static readonly Color NavajoWhite = Color.FromBgra(0xFFFFDEAD);

        /// <summary>
        /// Navy color.
        /// </summary>
        public static readonly Color Navy = Color.FromBgra(0xFF000080);

        /// <summary>
        /// OldLace color.
        /// </summary>
        public static readonly Color OldLace = Color.FromBgra(0xFFFDF5E6);

        /// <summary>
        /// Olive color.
        /// </summary>
        public static readonly Color Olive = Color.FromBgra(0xFF808000);

        /// <summary>
        /// OliveDrab color.
        /// </summary>
        public static readonly Color OliveDrab = Color.FromBgra(0xFF6B8E23);

        /// <summary>
        /// Orange color.
        /// </summary>
        public static readonly Color Orange = Color.FromBgra(0xFFFFA500);

        /// <summary>
        /// OrangeRed color.
        /// </summary>
        public static readonly Color OrangeRed = Color.FromBgra(0xFFFF4500);

        /// <summary>
        /// Orchid color.
        /// </summary>
        public static readonly Color Orchid = Color.FromBgra(0xFFDA70D6);

        /// <summary>
        /// PaleGoldenrod color.
        /// </summary>
        public static readonly Color PaleGoldenrod = Color.FromBgra(0xFFEEE8AA);

        /// <summary>
        /// PaleGreen color.
        /// </summary>
        public static readonly Color PaleGreen = Color.FromBgra(0xFF98FB98);

        /// <summary>
        /// PaleTurquoise color.
        /// </summary>
        public static readonly Color PaleTurquoise = Color.FromBgra(0xFFAFEEEE);

        /// <summary>
        /// PaleVioletRed color.
        /// </summary>
        public static readonly Color PaleVioletRed = Color.FromBgra(0xFFDB7093);

        /// <summary>
        /// PapayaWhip color.
        /// </summary>
        public static readonly Color PapayaWhip = Color.FromBgra(0xFFFFEFD5);

        /// <summary>
        /// PeachPuff color.
        /// </summary>
        public static readonly Color PeachPuff = Color.FromBgra(0xFFFFDAB9);

        /// <summary>
        /// Peru color.
        /// </summary>
        public static readonly Color Peru = Color.FromBgra(0xFFCD853F);

        /// <summary>
        /// Pink color.
        /// </summary>
        public static readonly Color Pink = Color.FromBgra(0xFFFFC0CB);

        /// <summary>
        /// Plum color.
        /// </summary>
        public static readonly Color Plum = Color.FromBgra(0xFFDDA0DD);

        /// <summary>
        /// PowderBlue color.
        /// </summary>
        public static readonly Color PowderBlue = Color.FromBgra(0xFFB0E0E6);

        /// <summary>
        /// Purple color.
        /// </summary>
        public static readonly Color Purple = Color.FromBgra(0xFF800080);

        /// <summary>
        /// Red color.
        /// </summary>
        public static Color Red => ColorsMap["Red"];

        /// <summary>
        /// RosyBrown color.
        /// </summary>
        public static readonly Color RosyBrown = Color.FromBgra(0xFFBC8F8F);

        /// <summary>
        /// RoyalBlue color.
        /// </summary>
        public static readonly Color RoyalBlue = Color.FromBgra(0xFF4169E1);

        /// <summary>
        /// SaddleBrown color.
        /// </summary>
        public static readonly Color SaddleBrown = Color.FromBgra(0xFF8B4513);

        /// <summary>
        /// Salmon color.
        /// </summary>
        public static readonly Color Salmon = Color.FromBgra(0xFFFA8072);

        /// <summary>
        /// SandyBrown color.
        /// </summary>
        public static readonly Color SandyBrown = Color.FromBgra(0xFFF4A460);

        /// <summary>
        /// SeaGreen color.
        /// </summary>
        public static readonly Color SeaGreen = Color.FromBgra(0xFF2E8B57);

        /// <summary>
        /// SeaShell color.
        /// </summary>
        public static readonly Color SeaShell = Color.FromBgra(0xFFFFF5EE);

        /// <summary>
        /// Sienna color.
        /// </summary>
        public static readonly Color Sienna = Color.FromBgra(0xFFA0522D);

        /// <summary>
        /// Silver color.
        /// </summary>
        public static readonly Color Silver = Color.FromBgra(0xFFC0C0C0);

        /// <summary>
        /// SkyBlue color.
        /// </summary>
        public static readonly Color SkyBlue = Color.FromBgra(0xFF87CEEB);

        /// <summary>
        /// SlateBlue color.
        /// </summary>
        public static readonly Color SlateBlue = Color.FromBgra(0xFF6A5ACD);

        /// <summary>
        /// SlateGray color.
        /// </summary>
        public static readonly Color SlateGray = Color.FromBgra(0xFF708090);

        /// <summary>
        /// Snow color.
        /// </summary>
        public static readonly Color Snow = Color.FromBgra(0xFFFFFAFA);

        /// <summary>
        /// SpringGreen color.
        /// </summary>
        public static readonly Color SpringGreen = Color.FromBgra(0xFF00FF7F);

        /// <summary>
        /// SteelBlue color.
        /// </summary>
        public static readonly Color SteelBlue = Color.FromBgra(0xFF4682B4);

        /// <summary>
        /// Tan color.
        /// </summary>
        public static readonly Color Tan = Color.FromBgra(0xFFD2B48C);

        /// <summary>
        /// Teal color.
        /// </summary>
        public static readonly Color Teal = Color.FromBgra(0xFF008080);

        /// <summary>
        /// Thistle color.
        /// </summary>
        public static readonly Color Thistle = Color.FromBgra(0xFFD8BFD8);

        /// <summary>
        /// Tomato color.
        /// </summary>
        public static readonly Color Tomato = Color.FromBgra(0xFFFF6347);

        /// <summary>
        /// Turquoise color.
        /// </summary>
        public static readonly Color Turquoise = Color.FromBgra(0xFF40E0D0);

        /// <summary>
        /// Violet color.
        /// </summary>
        public static readonly Color Violet = Color.FromBgra(0xFFEE82EE);

        /// <summary>
        /// Wheat color.
        /// </summary>
        public static readonly Color Wheat = Color.FromBgra(0xFFF5DEB3);

        /// <summary>
        /// White color.
        /// </summary>
        public static readonly Color White = Color.FromBgra(0xFFFFFFFF);

        /// <summary>
        /// WhiteSmoke color.
        /// </summary>
        public static readonly Color WhiteSmoke = Color.FromBgra(0xFFF5F5F5);

        /// <summary>
        /// Yellow color.
        /// </summary>
        public static readonly Color Yellow = Color.FromBgra(0xFFFFFF00);

        /// <summary>
        /// YellowGreen color.
        /// </summary>
        public static readonly Color YellowGreen = Color.FromBgra(0xFF9ACD32);
    }
}
