; JavaScript: declarations, classes and their members, and test blocks named by their string.
(function_declaration name: (identifier) @name body: (statement_block) @body) @definition.function
(generator_function_declaration name: (identifier) @name body: (statement_block) @body) @definition.function
(class_declaration name: (_) @name body: (class_body) @body) @definition.class
(method_definition name: (_) @name body: (statement_block) @body) @definition.method
(field_definition property: (_) @name) @definition.field
(program (lexical_declaration (variable_declarator name: (identifier) @name value: [(arrow_function body: (statement_block) @body) (function_expression body: (statement_block) @body)])) @definition.function)
(program (lexical_declaration (variable_declarator name: (identifier) @name)) @definition.variable)
(program (variable_declaration (variable_declarator name: (identifier) @name)) @definition.variable)
(export_statement declaration: (lexical_declaration (variable_declarator name: (identifier) @name value: [(arrow_function body: (statement_block) @body) (function_expression body: (statement_block) @body)])) @definition.function)
(export_statement declaration: (lexical_declaration (variable_declarator name: (identifier) @name)) @definition.variable)
((expression_statement (call_expression function: (identifier) @_callee arguments: (arguments . (string (string_fragment) @name) . [(arrow_function body: (statement_block) @body) (function_expression body: (statement_block) @body)]))) @definition.test
  (#any-of? @_callee "describe" "it" "test" "suite"))
; An object literal whose values include methods names their scope, like a class.
(pair key: (_) @name value: (object (method_definition)) @body) @definition.object
