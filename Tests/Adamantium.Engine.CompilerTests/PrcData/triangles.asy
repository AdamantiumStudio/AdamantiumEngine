// A tetrahedron as a triangle group: four known vertices, four faces, one red material.
import three;
unitsize(1bp);
currentprojection=orthographic(4,2,3);
triple[] v={(0,0,0),(2,0,0),(0,3,0),(0,0,4)};
int[][] vi={{0,2,1},{0,1,3},{0,3,2},{1,2,3}};
draw(v,vi,red);
