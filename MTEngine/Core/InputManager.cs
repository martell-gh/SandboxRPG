using Microsoft.Xna.Framework.Input;

namespace MTEngine.Core;

public class InputManager
{
    private KeyboardState _current, _previous;
    private MouseState _currentMouse, _previousMouse;

    public void Update()
    {
        _previous = _current;
        _current = Keyboard.GetState();
        _previousMouse = _currentMouse;
        _currentMouse = Mouse.GetState();
    }

    public bool IsDown(Keys key) => _current.IsKeyDown(key);
    public bool IsPressed(Keys key) => _current.IsKeyDown(key) && _previous.IsKeyUp(key);
    public bool IsReleased(Keys key) => _current.IsKeyUp(key) && _previous.IsKeyDown(key);

    public Microsoft.Xna.Framework.Point MousePosition => _currentMouse.Position;
    public bool LeftClicked => _currentMouse.LeftButton == ButtonState.Pressed
                            && _previousMouse.LeftButton == ButtonState.Released;
    public bool LeftReleased => _currentMouse.LeftButton == ButtonState.Released
                             && _previousMouse.LeftButton == ButtonState.Pressed;
    public bool RightClicked => _currentMouse.RightButton == ButtonState.Pressed
                             && _previousMouse.RightButton == ButtonState.Released;
    public bool RightReleased => _currentMouse.RightButton == ButtonState.Released
                              && _previousMouse.RightButton == ButtonState.Pressed;
    public bool LeftDown => _currentMouse.LeftButton == ButtonState.Pressed;
    public bool RightDown => _currentMouse.RightButton == ButtonState.Pressed;

    public int ScrollDelta => _currentMouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
}
