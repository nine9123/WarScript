# Running out of instructions is an InstructionBudgetExceeded error
# (plan T2.5).
# budget: 1000
x = 0
loop true
    x = x + 1
end
# expect-error: InstructionBudgetExceeded: Instruction budget exceeded
