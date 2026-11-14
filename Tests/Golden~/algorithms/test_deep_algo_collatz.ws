# test_deep_algo_collatz.ws — Collatz conjecture

fun collatz_steps [n]
    steps = 0
    loop n != 1
        if n % 2 == 0
            n = n / 2
        else
            n = 3 * n + 1
        end
        steps += 1
    end
    return steps
end

assert collatz_steps [1] == 0
assert collatz_steps [2] == 1
assert collatz_steps [6] == 8
assert collatz_steps [27] == 111
