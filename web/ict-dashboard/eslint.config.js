// ---------------------------------------------------------------------------------------------------
// ESLint flat config (ESLint 9/10) — the dashboard lint gate (CLAUDE.md: the React track must be
// "typecheck + lint clean"). Wires @eslint/js recommended + typescript-eslint recommended + the
// react-hooks (rules-of-hooks / exhaustive-deps) and react-refresh (Vite fast-refresh) plugins,
// scoped to the app source. Build output and the Node-side tool/config files are out of scope.
// ---------------------------------------------------------------------------------------------------

import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import globals from 'globals';

export default tseslint.config(
  // Never lint build artefacts, deps, or the generated OpenAPI types.
  {
    ignores: ['dist/**', 'node_modules/**', 'src/types/api.generated.ts'],
  },

  // App source — browser-targeted React + TypeScript.
  {
    files: ['src/**/*.{ts,tsx}'],
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    languageOptions: {
      ecmaVersion: 2022,
      globals: globals.browser,
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      // Vite fast-refresh only works when a module exports components; allow the colocated
      // constant exports the scaffold uses (e.g. ChartPanel's SYMBOLS/TIMEFRAMES next to <ChartPanel/>).
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      // Honor the `_`-prefix unused convention the repo already relies on (tsconfig noUnusedParameters):
      // a `_symbol`/`_timeframe` placeholder param is intentional, not a dead variable.
      '@typescript-eslint/no-unused-vars': [
        'error',
        {
          argsIgnorePattern: '^_',
          varsIgnorePattern: '^_',
          caughtErrorsIgnorePattern: '^_',
        },
      ],
    },
  },
);
