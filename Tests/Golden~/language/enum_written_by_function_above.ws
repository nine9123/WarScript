# T3.8: a function above an enum that writes the enum's name (declared global)
# is a compile error, though the parser reads it before the enum.
fun reset []
    global Mode
    Mode = 0   # expect-compile-error: Cannot reassign constant 'Mode'
end
enum Mode
    IDLE
    BUSY
end
print Mode :: BUSY
