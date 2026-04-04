using Microsoft.Xna.Framework.Input;

namespace MTEngine.Core;

public interface IKeyBindingSource
{
    Keys GetKey(string action);
}
