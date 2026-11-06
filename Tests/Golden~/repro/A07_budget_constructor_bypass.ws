# known-failing: BUDGET-1
# budget: 5000
# expect-error: InstructionBudgetExceeded
# Plan A.7 (T1.1, T1.5): v1 bypasses the budget inside constructors and prints "done".
class Burn [n]
    i = 0
    loop i < 300
        i += 1
    end
end
loop j in 0..200
    b = new Burn [0]
end
print "done"
