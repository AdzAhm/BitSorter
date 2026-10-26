// mux2.sv: a 2-to-1 multiplexer built from gates, as a course's first exercises build circuits:
// y = s ? b : a, from an inverter and three NAND gates, each with its own delays. An import draws it.
// Written for BitSorter's import tests (Part 13b); not taken from any course material.
module mux2 (
    input  logic a,
    input  logic b,
    input  logic s,
    output logic y
);
    logic s_n, n0, n1;

    INV   #(.Tpdlh(2), .Tpdhl(1)) g0 (.A(s), .Z(s_n));
    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g1 (.A(a), .B(s_n), .Z(n0));
    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g2 (.A(b), .B(s), .Z(n1));
    NAND2 #(.Tpdlh(3), .Tpdhl(2)) g3 (.A(n0), .B(n1), .Z(y));
endmodule
