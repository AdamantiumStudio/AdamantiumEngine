namespace Adamantium.DBus;

/// <summary>Walks D-Bus type signatures: where a complete type ends, and how its values align.</summary>
internal static class DBusSignature
{
    /// <summary>The index after the complete type that starts at <paramref name="index"/>.</summary>
    public static int End(string signature, int index)
    {
        var code = signature[index++];
        if (code == 'a')
        {
            return End(signature, index);
        }

        if (code is not ('(' or '{'))
        {
            return index;
        }

        var depth = 1;
        while (depth > 0)
        {
            var next = signature[index++];
            if (next is '(' or '{')
            {
                depth++;
            }
            else if (next is ')' or '}')
            {
                depth--;
            }
        }

        return index;
    }

    public static int Alignment(char code) => code switch
    {
        'y' or 'g' or 'v' => 1,
        'n' or 'q' => 2,
        'x' or 't' or 'd' or '(' or '{' => 8,
        _ => 4,
    };
}
