/**
 * jsPDF's built-in fonts have no peso sign: "₱800.00" printed as "±800.00".
 * PDFs spell it out instead ("PHP 800.00", "-PHP 5.00"); screens keep the sign.
 */
export function pdfText(text: string): string {
  return text.replace(/₱\s?/g, 'PHP ');
}
