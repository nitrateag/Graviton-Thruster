from vpython import *

# import time
from enum import Enum


class ThrusterElementType(Enum):
    artificialMass = 1
    generator = 2


class ThrusterElement:
    x = 0
    y = 0
    z = 0
    type = ThrusterElementType.artificialMass
    widthElem = 0.96

    def __init__(self, x, y, z, type=ThrusterElementType.artificialMass):
        self.x = x
        self.y = y
        self.z = z
        self.type = type

    def pos(self):
        return vector(self.x, self.y, self.z)


class artificialMass(ThrusterElement):
    mass = 50  # t
    #self.uiMass

    def __init__(self, x, y, z):
        super().__init__(x, y, z)

    def ui(self):
        self.uiMass =  box(pos=self.pos(), length=self.widthElem, height=self.widthElem, width=self.widthElem)
        return self.uiMass


class generator(ThrusterElement):
    up = 1;

    def __init__(self, x, y, z, diameter, height, up=1):
        super().__init__(x, y, z, ThrusterElementType.generator)
        self.up = up
        self.diameter = diameter
        self.height = height


    def pos(self):
        return vector(self.x, self.y, self.z)

    def ui(self):
        sphere(pos = self.pos(), radius = 0.2)
        self.uiGen = pyramid(pos=self.pos() - vector(0,0,self.up/2),
                             length=self.widthElem, height=self.widthElem, width=self.widthElem,
                             color=color.red)
        self.uiGen.rotate(-3.14/2*self.up, vector(0,1,0))
        self.uiRange = box(pos=vector(self.x,  # - self.diameter/2,
                                      self.y,  # -(0.5) ,#- self.diameter/2,
                                      self.z),  # - self.height/2),
                           length=self.diameter+0.5,
                           height=self.diameter + 0.5,
                           width=self.height+1,
                           color=color.green,
                           opacity=0.2)
        self.uiRange.visible = False

    def enable(self, enable=True):
        #self.uiRange.visible = enable
        if enable:
            self.uiGen.color = color.green
        else:
            self.uiGen.color = color.red


class thruster:
    diameter = 3
    height = 9

    volume = 0
    massArt = 0
    nbMass = 0
    nbGenerator = 0

    def __init__(self, height, diameter=3):
        self.elem = [[[]]]
        self.height = height
        self.diameter = diameter
        self.open = height % 2

        for x in range(self.diameter):
            self.elem.append([[]])
            for y in range(self.diameter):
                self.elem[x].append([])
                for z in range(self.height):
                    self.elem[x][y].append(artificialMass(x, y, z))
                    self.massArt += self.elem[x][y][z].mass
                    ++self.nbMass

        self.middle = trunc((self.diameter - 1) / 2)

        for z in range(self.height):
            if (z + self.open) % 3 == 0:
                self.massArt -= self.elem[self.middle][self.middle][z].mass
                self.elem[self.middle][self.middle][z] = generator(self.middle, self.middle, z, diameter,
                                                                   min(z, height - z - 1) * 2, -1)
            elif (z + self.open) % 3 == 2:
                self.massArt -= self.elem[self.middle][self.middle][z].mass
                self.elem[self.middle][self.middle][z] = generator(self.middle, self.middle, z, diameter,
                                                                   min(z, height - z - 1) * 2)
            else:
                continue
            --self.nbMass
            ++self.nbGenerator

        self.volume = self.height * self.diameter ** 2


    def massInGravFeild(self, gen):
        for x in range(gen.diameter):
            for y in range(gen.diameter):
                for z in range(gen.z - trunc(gen.height/2), gen.z + trunc(gen.height/2)+1, 1):
                    if self.elem[x][y][z].type == ThrusterElementType.artificialMass:
                        yield self.elem[x][y][z]

    def ui(self):
        for x in range(self.diameter):
            for y in range(self.diameter):
                for z in range(self.height):
                    if x <= self.middle - 1 or y <= self.middle - 1 or (x == self.middle and y == self.middle):
                        self.elem[x][y][z].ui()
                    elif z > self.height / 3:
                        self.elem[x][y][z].ui().opacity = 0.1
                    else:
                        self.elem[x][y][z].ui().opacity = 0.1

    def uiAnim(self):
        for i in range(2):
            while True:
                for z in range(self.height):
                    gen = self.elem[self.middle][self.middle][z]
                    if gen.type == ThrusterElementType.generator:
                        gen.enable()
                        for mass in self.massInGravFeild(gen):
                            mass.uiMass.color = vector(0.75,1,0.75)
                        sleep(2)
                        for mass in self.massInGravFeild(gen):
                            mass.uiMass.color = color.white
                        gen.enable(False)
                        # print("scene.camera.axis", scene.camera.axis)
                        # print('scene.camera.pos', scene.camera.pos)
                        print("scene.forward", scene.forward)
                        print('scene.center', scene.center)





