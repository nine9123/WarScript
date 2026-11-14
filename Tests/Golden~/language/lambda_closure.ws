# T3.4 (PROP-16): lambdas have no closures, so reading a local or parameter
# of the code around one is an error ("closures are not supported").
fun make_adder [n]
    return fun [x] return x + n end   # expect-compile-error: closures are not supported: 'n'
end
