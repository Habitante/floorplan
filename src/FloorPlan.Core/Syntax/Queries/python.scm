; Python: module and class assignments are the data; decorators join their definition.
(module (expression_statement (assignment left: (identifier) @name)) @definition.variable)
(class_definition body: (block (expression_statement (assignment left: (identifier) @name)) @definition.variable))
(class_definition name: (identifier) @name body: (block) @body) @definition.class
(function_definition name: (identifier) @name body: (block) @body) @definition.function
((module (if_statement condition: (comparison_operator (identifier) @_lhs) consequence: (block) @body) @definition.main)
  (#eq? @_lhs "__name__"))
