namespace Dame.Win32.Input;

[Flags]
internal enum HotKeyModifiers : uint
{
    None = 0x0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}
