# A raise statement is a Raised error at its line, with the raised value as
# its message (plan T2.5).
fun fail []
    raise "boom"   # expect-error: Raised: boom
end
fail []
