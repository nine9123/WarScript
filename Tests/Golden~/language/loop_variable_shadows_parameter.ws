# T3.4 (PROP-9): a loop variable may not shadow a parameter of its body.
fun count_up [n]
    loop n in 0..3   # expect-compile-error: loop variable 'n' shadows a parameter of the same body
        print n
    end
end
