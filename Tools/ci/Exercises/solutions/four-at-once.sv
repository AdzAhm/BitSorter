// Four at once: a solution, from the one its level's own tests proved (Part 15c).

// FA: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module FA (
    input logic a,
    input logic b,
    input logic cin,
    output logic s,
    output logic cout
);
    logic g1_y, g2_y, g4_y;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a), .B(b), .Z(g1_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(a), .B(b), .Z(g2_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(g1_y), .B(cin), .Z(s));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(g1_y), .B(cin), .Z(g4_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g5 (.A(g2_y), .B(g4_y), .Z(cout));
endmodule

// four_at_once: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module four_at_once (
    input logic a3,
    input logic a2,
    input logic a1,
    input logic a0,
    input logic b3,
    input logic b2,
    input logic b1,
    input logic b0,
    input logic cin,
    output logic s3,
    output logic s2,
    output logic s1,
    output logic s0,
    output logic cout
);
    logic u1_cout, u2_cout, u3_cout;
    FA u1 (
        .a(a0),
        .b(b0),
        .cin(cin),
        .s(s0),
        .cout(u1_cout)
    );
    FA u2 (
        .a(a1),
        .b(b1),
        .cin(u1_cout),
        .s(s1),
        .cout(u2_cout)
    );
    FA u3 (
        .a(a2),
        .b(b2),
        .cin(u2_cout),
        .s(s2),
        .cout(u3_cout)
    );
    FA u4 (
        .a(a3),
        .b(b3),
        .cin(u3_cout),
        .s(s3),
        .cout(cout)
    );
endmodule
