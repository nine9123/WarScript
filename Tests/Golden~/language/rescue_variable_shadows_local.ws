# T3.4 (PROP-9): a rescue variable may not shadow a local of its body.
fun f []
    err = "none"
    begin
        raise "boom"
    rescue err   # expect-compile-error: rescue variable 'err' shadows a local variable of the same body
        print err
    end
end
