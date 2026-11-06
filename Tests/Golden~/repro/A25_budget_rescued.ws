# known-failing: BUDGET-2
# budget: 1000
# Plan A.25 (found by the fuzzer, T0.7): v1 disarms the instruction budget
# once it runs out (a budget of 0 means unlimited), so after a rescue the
# script runs without limit. With an infinite loop instead of the 100,000
# iterations below, the host hangs. v2 keeps the budget exhausted, so the
# error ends the run whatever the script rescues.
begin
    loop true
    end
rescue e
end
n = 0
loop i in 0..100000
    n += 1
end
print n
# expect-error: InstructionBudgetExceeded
