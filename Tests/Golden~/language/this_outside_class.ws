# T3.8 (PROP-29): 'this' and '@x' outside every class are a compile error
# (a NameError at runtime before). Methods and class bodies may use them
# (this_in_frames.ws).
fun describe []
    return "x is {@x}"   # expect-compile-error: 'this' and '@' fields can only be used inside a class
end
print describe []
