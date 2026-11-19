// ESLint (flat config): the rules of hooks and accessibility in JSX, on top of the TypeScript recommendations.
// Real defects are errors; the rest are warnings held by a ratchet (--max-warnings in package.json) so the count only goes down.
import js from "@eslint/js";
import tseslint from "typescript-eslint";
import reactHooks from "eslint-plugin-react-hooks";
import jsxA11y from "eslint-plugin-jsx-a11y";
import globals from "globals";

export default tseslint.config(
  { ignores: ["dist", "node_modules", "src/components/ui/**"] },
  {
    files: ["src/**/*.{ts,tsx}"],
    extends: [js.configs.recommended, ...tseslint.configs.recommended, jsxA11y.flatConfigs.recommended],
    plugins: { "react-hooks": reactHooks },
    languageOptions: { ecmaVersion: 2022, globals: globals.browser },
    rules: {
      ...reactHooks.configs.recommended.rules,
      "react-hooks/rules-of-hooks": "error",
      "react-hooks/exhaustive-deps": "warn",
      "@typescript-eslint/no-unused-vars": ["error", { argsIgnorePattern: "^_", varsIgnorePattern: "^_" }],
      // Focusing the first field of an antd dialog is intended; raw DOM elements still may not autofocus.
      "jsx-a11y/no-autofocus": ["error", { ignoreNonDOM: true }],
      // Composite widgets (dialog, menu, tablist, carousel, grid rows with their own button) take keys on the container
      // and focus on their children; these rules flag that pattern, so they warn and stay under the ratchet for review.
      "jsx-a11y/no-noninteractive-element-interactions": "warn",
      "jsx-a11y/interactive-supports-focus": "warn",
      "jsx-a11y/click-events-have-key-events": "warn",
    },
  },
);
