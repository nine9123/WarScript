# test_deep_algo_sieve.ws — Sieve of Eratosthenes

fun sieve [limit]
    is_prime = {}
    loop i in 0..limit + 1
        if i < 2
            is_prime << false
        else
            is_prime << true
        end
    end

    loop i in 2..limit + 1
        if is_prime{i}
            j = i * 2
            loop j <= limit
                is_prime{j} = false
                j += i
            end
        end
    end

    primes = {}
    loop i in 2..limit + 1
        if is_prime{i}
            primes << i
        end
    end
    return primes
end

primes = sieve [30]
assert primes == {2, 3, 5, 7, 11, 13, 17, 19, 23, 29}
