chemistry-trial-window-title = Chemistry Time Trial

## Configuration

chemistry-trial-duration = Time trial length
chemistry-trial-minutes = min
chemistry-trial-seconds = s
chemistry-trial-reveal-duration = Delay after a wrong answer
chemistry-trial-categories = Reagent categories
chemistry-trial-categories-all = All
chemistry-trial-categories-none = None
chemistry-trial-category = {$category} ({$count})
chemistry-trial-eligible = {$count ->
    [one] 1 recipe in the pool
    *[other] {$count} recipes in the pool
}
chemistry-trial-start = Start

## Time trial

chemistry-trial-time-left = Time left: {$time}
chemistry-trial-score = Passed: {$passed}   Failed: {$failed}
chemistry-trial-abort = Abort
chemistry-trial-submit = Submit

chemistry-trial-prompt-recipe = Which reactants, in which amounts, at which temperature make:
chemistry-trial-question-recipe = [font size=16][bold]{$product}[/bold][/font]

chemistry-trial-prompt-products = What do these reactants make, in which amounts:
chemistry-trial-question-products = [font size=16][bold]{$reactants}[/bold][/font]
    Temperature: {$temperature}
    Mixing: {$mixing}

chemistry-trial-reactants = Reactants
chemistry-trial-reactants-placeholder = e.g. 1 Carbon + 1 Welding Fuel + 1 Hydrogen
chemistry-trial-products = Products
chemistry-trial-products-placeholder = e.g. 3 Oil
chemistry-trial-temperature = Temperature (K)
chemistry-trial-temperature-placeholder = e.g. 375-621, 375+ or leave empty for any

chemistry-trial-reveal-title = Wrong! The correct recipe is:
chemistry-trial-reveal = [bold]{$label}[/bold]
    {$reactants} = {$products}
    Temperature: {$temperature}
    Mixing: {$mixing}
    Your answer: [color=gray]{$answer}[/color]
chemistry-trial-reveal-countdown = Continuing in {$seconds}...
chemistry-trial-answer-with-temperature = {$amounts} at {$temperature}
chemistry-trial-answer-empty = (nothing)

## Results

chemistry-trial-results = Time's up!
chemistry-trial-results-passed = Passed: {$count}
chemistry-trial-results-failed = Failed: {$count}
chemistry-trial-results-total = Total: {$count}
chemistry-trial-dismiss = Back

## Recipes

chemistry-trial-recipe-index = {$amount} {$name} (R{$index})
chemistry-trial-amount = {$amount} {$name}
chemistry-trial-amount-catalyst = {$amount} {$name} (catalyst)
chemistry-trial-mixing-none = none
chemistry-trial-temperature-any = any
chemistry-trial-temperature-minimum = {$min}+ K
chemistry-trial-temperature-range = {$min}-{$max} K

