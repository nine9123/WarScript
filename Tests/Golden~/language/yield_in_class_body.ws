# T3.3 (PROP-21): a class body runs as a constructor frame and cannot
# suspend. A function it calls still can (BytecodeCoroutineTests).
class P [x]
    if x > 0
        yield   # expect-compile-error: 'yield' is not allowed in a class body
    end
end
