// Look ahead: a solution, from the one its level's own tests proved (Part 15c).

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

// LA: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module LA (
    input logic a0,
    input logic b0,
    input logic cin,
    input logic a1,
    input logic b1,
    output logic cout
);
    logic g1_y, g2_y, g3_y, g4_y, g5_y, g6_y, g7_y, g8_y;
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g1 (.A(a0), .B(b0), .Z(g1_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g2 (.A(a0), .B(b0), .Z(g2_y));
    XOR2 #(.Tpdlh(1), .Tpdhl(1)) g3 (.A(a1), .B(b1), .Z(g3_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g4 (.A(a1), .B(b1), .Z(g4_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g5 (.A(g3_y), .B(g2_y), .Z(g5_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g6 (.A(g3_y), .B(g1_y), .Z(g6_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g7 (.A(g4_y), .B(g5_y), .Z(g7_y));
    AND2 #(.Tpdlh(1), .Tpdhl(1)) g8 (.A(g6_y), .B(cin), .Z(g8_y));
    OR2 #(.Tpdlh(1), .Tpdhl(1)) g9 (.A(g7_y), .B(g8_y), .Z(cout));
endmodule

// look_ahead: drawn in BitSorter. Gates with a delay are cells of bitsorter_cells.sv.

module look_ahead (
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
    logic u1_cout, u5_cout, u3_cout;
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
        .cout()
    );
    FA u3 (
        .a(a2),
        .b(b2),
        .cin(u5_cout),
        .s(s2),
        .cout(u3_cout)
    );
    FA u4 (
        .a(a3),
        .b(b3),
        .cin(u3_cout),
        .s(s3),
        .cout()
    );
    LA u5 (
        .a0(a0),
        .b0(b0),
        .cin(cin),
        .a1(a1),
        .b1(b1),
        .cout(u5_cout)
    );
    LA u6 (
        .a0(a2),
        .b0(b2),
        .cin(u5_cout),
        .a1(a3),
        .b1(b3),
        .cout(cout)
    );
endmodule
