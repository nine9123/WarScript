# T3.4 (PROP-9): a loop variable may not shadow a local of its body, even
# one assigned after the loop.
fun f []
    loop x in 0..3   # expect-compile-error: loop variable 'x' shadows a local variable of the same body
        print x
    end
    x = 42
    return x
end
