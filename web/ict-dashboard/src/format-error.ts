// ---------------------------------------------------------------------------------------------------
// errorMessage — turn a React Query error (unknown) into a short, human-readable string for the inline
// panel error states (plan §6.3 "fail hard, VISIBLY"). A failed/erroring query must surface the error,
// not silently degrade to a blank/loading panel — a down host must look different from a healthy one.
// ---------------------------------------------------------------------------------------------------

export function errorMessage(error: unknown): string {
  if (error instanceof Error && error.message) {
    return error.message;
  }
  if (typeof error === 'string' && error) {
    return error;
  }
  return 'host error';
}
