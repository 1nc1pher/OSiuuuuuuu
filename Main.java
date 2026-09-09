//strategy

// ===============================
// 1. STRATEGY INTERFACE
// ===============================

// Defines the common operation that
// all strategies must provide.
interface Strategy {
    void execute();
}


// ===============================
// 2. CONCRETE STRATEGY A
// ===============================

class StrategyA implements Strategy {

    @Override
    public void execute() {
        // Algorithm / behavior A
        System.out.println("Executing Strategy A");
    }
}


// ===============================
// 3. CONCRETE STRATEGY B
// ===============================

class StrategyB implements Strategy {

    @Override
    public void execute() {
        // Algorithm / behavior B
        System.out.println("Executing Strategy B");
    }
}


// ===============================
// 4. CONTEXT
// ===============================

class Context {

    // Context HAS-A Strategy
    // This is composition.
    private Strategy strategy;

    // Strategy can be supplied from outside.
    public Context(Strategy strategy) {
        this.strategy = strategy;
    }

    // Can also change strategy later.
    public void setStrategy(Strategy strategy) {
        this.strategy = strategy;
    }

    // Context delegates the actual work
    // to the selected strategy.
    public void performOperation() {
        strategy.execute();
    }
}


// ===============================
// 5. CLIENT
// ===============================

public class Main {

    public static void main(String[] args) {

        // Select Strategy A
        Context context =
            new Context(new StrategyA());

        context.performOperation();
        // Output: Executing Strategy A


        // Change strategy at runtime
        context.setStrategy(new StrategyB());

        context.performOperation();
        // Output: Executing Strategy B
    }
}


// template

abstract class AbstractClass {

    // Template Method
    // Defines the overall algorithm/skeleton.
    public final void templateMethod() {

        step1();       // Common step
        step2();       // Varying step
        step3();       // Common step
        step4();       // Varying step
    }

    // Common operation
    // Same implementation for all subclasses.
    private void step1() {
        System.out.println("Common step 1");
    }

    // Varying operation
    // Subclasses provide their own implementation.
    protected abstract void step2();

    // Common operation
    private void step3() {
        System.out.println("Common step 3");
    }

    // Varying operation
    protected abstract void step4();
}

class ConcreteClassA extends AbstractClass {

    @Override
    protected void step2() {
        System.out.println("A's implementation");
    }

    @Override
    protected void step4() {
        System.out.println("A's implementation");
    }
}

class ConcreteClassB extends AbstractClass {

    @Override
    protected void step2() {
        System.out.println("B's implementation");
    }

    @Override
    protected void step4() {
        System.out.println("B's implementation");
    }
}

public class Main {

    public static void main(String[] args) {

        AbstractClass obj = new ConcreteClassA();

        obj.templateMethod();

        // The same algorithm skeleton is used,
        // but A's versions of the varying steps run.
    }
}

//observer

// ========================================
// 1. OBSERVER INTERFACE
// ========================================

interface Observer {

    // Called when the Subject changes.
    void update();
}


// ========================================
// 2. SUBJECT INTERFACE
// ========================================

interface Subject {

    // Add an observer.
    void registerObserver(Observer observer);

    // Remove an observer.
    void removeObserver(Observer observer);

    // Notify all registered observers.
    void notifyObservers();
}


// ========================================
// 3. CONCRETE SUBJECT
// ========================================

class ConcreteSubject implements Subject {

    // List of observers interested in this Subject.
    private List<Observer> observers = new ArrayList<>();

    // Some state maintained by the Subject.
    private int state;


    @Override
    public void registerObserver(Observer observer) {
        observers.add(observer);
    }


    @Override
    public void removeObserver(Observer observer) {
        observers.remove(observer);
    }


    @Override
    public void notifyObservers() {

        // Notify every registered observer.
        for (Observer observer : observers) {
            observer.update();
        }
    }


    // Change the Subject's state.
    public void setState(int state) {

        this.state = state;

        // State changed → notify observers.
        notifyObservers();
    }


    public int getState() {
        return state;
    }
}


// ========================================
// 4. CONCRETE OBSERVER
// ========================================

class ConcreteObserver implements Observer {

    @Override
    public void update() {

        // React to the Subject's change.
        System.out.println("Observer updated!");
    }
}

public class Main {

    public static void main(String[] args) {

        ConcreteSubject subject = new ConcreteSubject();

        ConcreteObserver observer1 = new ConcreteObserver();
        ConcreteObserver observer2 = new ConcreteObserver();


        // Subscribe observers.
        subject.registerObserver(observer1);
        subject.registerObserver(observer2);


        // State changes.
        // Both observers get notified.
        subject.setState(10);


        // Unsubscribe observer1.
        subject.removeObserver(observer1);


        // Now only observer2 is notified.
        subject.setState(20);
    }
}


// observer core code

interface Observer {
    void update();
}

interface Subject {
    void registerObserver(Observer o);
    void removeObserver(Observer o);
    void notifyObservers();
}

class ConcreteSubject implements Subject {

    List<Observer> observers;

    public void registerObserver(Observer o) {
        observers.add(o);
    }

    public void removeObserver(Observer o) {
        observers.remove(o);
    }

    public void notifyObservers() {
        for (Observer o : observers)
            o.update();
    }
}

class ConcreteObserver implements Observer {

    public void update() {
        // React to change
    }
}

//mediator


// ========================================
// 1. MEDIATOR INTERFACE
// ========================================

interface Mediator {

    // Called by components when
    // something happens.
    void notify(Component sender, String event);
}


// ========================================
// 2. CONCRETE MEDIATOR
// ========================================

class ConcreteMediator implements Mediator {

    private ComponentA componentA;
    private ComponentB componentB;


    // Mediator knows the components
    // that it coordinates.
    public ConcreteMediator(
            ComponentA componentA,
            ComponentB componentB) {

        this.componentA = componentA;
        this.componentB = componentB;
    }


    @Override
    public void notify(Component sender, String event) {

        // Centralized interaction logic.
        // Decide which component should
        // respond to the event.

        if (sender == componentA) {

            if (event.equals("eventA")) {
                componentB.doSomething();
            }
        }

        else if (sender == componentB) {

            if (event.equals("eventB")) {
                componentA.doSomething();
            }
        }
    }
}


// ========================================
// 3. BASE COMPONENT
// ========================================

abstract class Component {

    protected Mediator mediator;


    public Component(Mediator mediator) {
        this.mediator = mediator;
    }
}


// ========================================
// 4. CONCRETE COMPONENT A
// ========================================

class ComponentA extends Component {

    public ComponentA(Mediator mediator) {
        super(mediator);
    }


    public void doSomething() {

        // Instead of directly calling ComponentB,
        // notify the mediator.

        mediator.notify(this, "eventA");
    }
}


// ========================================
// 5. CONCRETE COMPONENT B
// ========================================

class ComponentB extends Component {

    public ComponentB(Mediator mediator) {
        super(mediator);
    }


    public void doSomething() {

        mediator.notify(this, "eventB");
    }
}


// state

// ========================================
// 1. STATE INTERFACE
// ========================================

interface State {

    // Operation whose behavior depends
    // on the current state.
    void handle();
}


// ========================================
// 2. CONCRETE STATE A
// ========================================

class StateA implements State {

    @Override
    public void handle() {

        System.out.println("Behavior in State A");
    }
}


// ========================================
// 3. CONCRETE STATE B
// ========================================

class StateB implements State {

    @Override
    public void handle() {

        System.out.println("Behavior in State B");
    }
}


// ========================================
// 4. CONTEXT
// ========================================

class Context {

    // The Context stores its current state.
    private State state;


    public Context(State initialState) {
        this.state = initialState;
    }


    // Change the current state.
    public void setState(State state) {
        this.state = state;
    }


    // Delegate behavior to the current state.
    public void request() {
        state.handle();
    }
}


public class Main {

    public static void main(String[] args) {

        Context context =
            new Context(new StateA());

        // Behavior of State A
        context.request();


        // Change state
        context.setState(new StateB());

        // Now behavior changes
        context.request();
    }
}