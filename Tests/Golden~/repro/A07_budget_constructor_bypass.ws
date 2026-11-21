# budget: 5000
# expect-error: InstructionBudgetExceeded
# Plan A.7 (T1.1, T1.5): v1 bypassed the budget inside constructors and printed
# "done"; since T1.1 constructor bodies run on the same VM and charge its budget.
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
