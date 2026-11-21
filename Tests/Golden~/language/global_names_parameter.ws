# T3.4 (PROP-10): a parameter cannot be declared global.
fun f [n]
    global n   # expect-compile-error: 'n' is a parameter and cannot be declared global
end
